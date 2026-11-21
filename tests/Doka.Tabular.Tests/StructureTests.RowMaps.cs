using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Tests;

public sealed partial class StructureTests
{
    [Fact]
    public async Task RowMap_GivesEveryRoleItsOwnInstanceAndRejectsTheWholeRow()
    {
        // Arrange
        var rows = RowMap.Create();
        var user = rows.Add(TabularMap
            .For<Person>()
            .Column(x => x.Name, index: 0)
            .Build());

        var address = rows.Add(TabularMap
            .For<Located>()
            .Column(x => x.City, index: 1, configure: c => c.Required())
            .Column(x => x.PostalCode, index: 2)
            .Build());

        var map = rows.Build();

        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8("Ada,Dresden,01067\nBob,,53111"),
            map,
            new CsvImportOptions { Strategy = ImportStrategy.Partial },
            Token);

        // Assert
        var row = Assert.Single(report.Items!);
        Assert.Equal(2, row.Items.Count);
        Assert.Same(row.Items[0], row.Get(user));
        Assert.Same(row.Items[1], row.Get(address));
        Assert.Equal(2L,
            Assert.Single(report.Errors)
                .Location.RecordNumber);
    }

    [Fact]
    public async Task RowMap_ItemsOfARow_CannotBeChanged()
    {
        // Arrange
        var rows = RowMap.Create();
        _ = rows.Add(TabularMap
            .For<Person>()
            .Column(x => x.Name, index: 0)
            .Build());

        _ = rows.Add(TabularMap
            .For<Located>()
            .Column(x => x.City, index: 1, configure: c => c.Required())
            .Column(x => x.PostalCode, index: 2)
            .Build());

        var map = rows.Build();
        var report = await CsvInstance.ImportAsync(TestInput.Utf8("Ada,Dresden,01067\nBob,,53111"),
            map,
            new CsvImportOptions { Strategy = ImportStrategy.Partial },
            Token);

        // Act
        var exception = Record.Exception(() => ((IList<object>)report.Items![0].Items).RemoveAt(0));

        // Assert
        Assert.IsType<NotSupportedException>(exception);
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task RowMap_RolesOfTheSameTypeExportAndReimportByHeaderName(
        TabularType type
    )
    {
        // Arrange
        var rows = RowMap.Create();
        var billing = rows.Add(s_located,
            e => e
                .StartIndex(0)
                .KeyPrefix(ColumnKey.Create("billing"))
                .HeaderPrefix("Rechnungs-"));

        var shipping = rows.Add(s_located,
            e => e
                .StartIndex(2)
                .KeyPrefix(ColumnKey.Create("shipping"))
                .HeaderPrefix("Liefer-"));

        var map = rows.Build();
        var export = map
            .From<OrderExportRow>()
            .Bind(billing, x => x.Billing)
            .Bind(shipping, x => x.Shipping)
            .Build();

        var source = new[] { new OrderExportRow(new Located("Dresden", "01067"), new Located("Bonn", "53111")) };

        // Act
        // WHY: The round trip of the own output is the checked contract, so export and reimport form one action.
        var (_, file) =
            await Export.ExportAsync(source, export, type, new TabularExportOptions { WriteHeader = true }, Token);

        var reimport = await Import.ImportAsync(file!,
            type,
            map,
            new TabularImportOptions { Binding = ColumnBinding.ByHeaderName },
            Token);

        // Assert
        Assert.Equal(["Rechnungs-Ort", "Rechnungs-PLZ", "Liefer-Ort", "Liefer-PLZ"], Cells(type, file!)[0]);
        var row = Assert.Single(reimport.Items!);
        Assert.Equal(("Dresden", "Bonn"),
            (row.Get(billing)
                .City, row.Get(shipping)
                .City));
    }

    [Fact]
    public async Task RowMap_LayoutOfAFullyDeselectedRole_IsAKnownKeyWithoutAColumn()
    {
        // Arrange
        var rows = RowMap.Create();
        var billing = rows.Add(s_located, e => e.KeyPrefix(ColumnKey.Create("billing")));
        var shipping = rows.Add(s_located,
            e => e
                .StartIndex(2)
                .KeyPrefix(ColumnKey.Create("shipping")));

        var export = rows
            .Build()
            .From<OrderExportRow>()
            .Bind(billing, x => x.Billing)
            .Bind(shipping, x => x.Shipping)
            .Build();

        // Act
        var report = await ExcelInstance.Format.ExportAsync(
            new[] { new OrderExportRow(new Located("Dresden", "01067"), new Located("Bonn", "53111")) },
            export,
            new MemoryStream(),
            new ExcelExportOptions
            {
                ColumnKeys = [ColumnKey.Create("billing.city"), ColumnKey.Create("billing.postalCode")],
                Layout = new ExcelLayoutOptions
                {
                    Columns = new Dictionary<ColumnKey, ExcelColumnLayout>
                    {
                        [ColumnKey.Create("shipping.city")] = new() { Width = 20 },
                    },
                },
            },
            Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task RowMap_RolesAreReadInRegistrationOrderWhateverTheirIndexes(
        TabularType type
    )
    {
        // Arrange
        var reversed = RowMap.Create();
        var second = reversed.Add(s_located,
            e => e
                .StartIndex(2)
                .KeyPrefix(ColumnKey.Create("shipping")));

        var first = reversed.Add(s_located, e => e.KeyPrefix(ColumnKey.Create("billing")));

        // Act
        var swapped = await Import.ImportAsync(Create(type, ["Dresden", "01067", "Bonn", "53111"]),
            type,
            reversed.Build(),
            Token);

        // Assert
        Assert.Equal(("Dresden", "Bonn"),
            (swapped.Items![0]
                .Get(first)
                .City, swapped
                .Items[0]
                .Get(second)
                .City));

        Assert.Equal("Bonn",
            ((Located)swapped
                .Items[0]
                .Items[0]).City);
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task RowMap_SharedSourceColumn_ImportsBothRoles(
        TabularType type
    )
    {
        // Arrange
        var (map, user, address, _) = SharedSourceColumnMaps();

        // Act
        var report = await Import.ImportAsync(Create(type, [7, "Ada", "Dresden"]), type, map, Token);

        // Assert
        Assert.Equal((7, 7),
            (report.Items![0]
                .Get(user)
                .Id, report
                .Items[0]
                .Get(address)
                .UserId));
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task RowMap_SharedSourceColumn_WrittenByTwoRoles_IsAConfigurationError(
        TabularType type
    )
    {
        // Arrange
        var (map, _, _, _) = SharedSourceColumnMaps();
        var rows = (await Import.ImportAsync(Create(type, [7, "Ada", "Dresden"]), type, map, Token)).Items!;

        // Act
        var (report, file) = await Export.ExportAsync(rows, map, type, Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Configuration.Invalid, report.FatalError?.Code ?? report.Errors[0].Code);
        Assert.Null(file);
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task RowMap_SharedSourceColumn_WithAnImportOnlyRole_Exports(
        TabularType type
    )
    {
        // Arrange
        var (map, _, _, importOnly) = SharedSourceColumnMaps();
        var rows = (await Import.ImportAsync(Create(type, [7, "Ada", "Dresden"]), type, map, Token)).Items!;

        // Act
        var (report, _) = await Export.ExportAsync(rows, importOnly, type, Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
    }

    private static (RowMap Map, TabularRole<IdUser> User, TabularRole<IdAddress> Address, RowMap ImportOnly)
        SharedSourceColumnMaps()
    {
        var userMap = TabularMap
            .For<IdUser>()
            .Column(x => x.Id, index: 0)
            .Column(x => x.Name, index: 1)
            .Build();

        var addressMap = TabularMap
            .For<IdAddress>()
            .Column(x => x.UserId, index: 0, key: ColumnKey.Create("userId"))
            .Column(x => x.City, index: 2)
            .Build();

        var both = RowMap.Create();
        var user = both.Add(userMap);
        var address = both.Add(addressMap);
        var importOnly = RowMap.Create();
        importOnly.Add(userMap);
        importOnly.Add(addressMap, e => e.ImportOnly());

        return (both.Build(), user, address, importOnly.Build());
    }
}
