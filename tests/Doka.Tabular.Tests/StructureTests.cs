using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Tests;

/// <summary>Nested maps, header-name import, row maps with several DTOs, and composed values.</summary>
public sealed partial class StructureTests
{
    private static readonly TabularMap<AddressDto> s_address = TabularMap
        .For<AddressDto>()
        .Column(x => x.City, index: 0, key: ColumnKey.Create("city"), configure: c => c.Header("Ort"))
        .Column(x => x.PostalCode, index: 1, key: ColumnKey.Create("postalCode"), configure: c => c.Header("PLZ"))
        .Column(x => x.Street, index: 3, key: ColumnKey.Create("street"), configure: c => c.Header("Straße"))
        .Build();

    private static readonly TabularMap<AddressDto> s_requiredAddress = TabularMap
        .For<AddressDto>()
        .Column(x => x.City, index: 0, key: ColumnKey.Create("city"), configure: c => c.Required())
        .Column(x => x.PostalCode, index: 1, key: ColumnKey.Create("postalCode"))
        .Column(x => x.Street, index: 3, key: ColumnKey.Create("street"), configure: c => c.Required())
        .Build();

    private static readonly TabularMap<Located> s_located = TabularMap
        .For<Located>()
        .Column(
            x => x.City,
            index: 0,
            key: ColumnKey.Create("city"),
            configure: c => c
                .Header("Ort")
                .HeaderTranslationKey("ort"))
        .Column(
            x => x.PostalCode,
            index: 1,
            key: ColumnKey.Create("postalCode"),
            configure: c => c
                .Header("PLZ")
                .HeaderTranslationKey("plz"))
        .Build();

    private static readonly string[] s_sixteenFields =
    [
        .. Enumerable
            .Range(0, 16)
            .Select(index => $"v{index}"),
    ];

    private static readonly TabularMap<Order> s_shiftedOrder = TabularMap
        .For<Order>()
        .Column(x => x.Number, index: 0)
        .Nested(
            x => x.Billing,
            s_address,
            e => e
                .StartIndex(5)
                .KeyPrefix(ColumnKey.Create("billing"))
                .HeaderPrefix("Rechnung "))
        .Nested(
            x => x.Shipping,
            s_address,
            e => e
                .StartIndex(12)
                .KeyPrefix(ColumnKey.Create("shipping"))
                .HeaderPrefix("Lieferung "))
        .Build();

    private static readonly User[] s_dresdenUsers = [new("Ada", new AddressDto("Dresden", "01067", "Hauptstraße"))];

    private static readonly TabularMap<User> s_userWithAddress = TabularMap
        .For<User>()
        .Column(x => x.Name, index: 0)
        .Nested(x => x.Address, s_address, e => e.StartIndex(1))
        .Build();

    private static readonly TabularMap<User> s_userWithRequiredAddress = TabularMap
        .For<User>()
        .Column(x => x.Name, index: 0)
        .Nested(x => x.Address, s_requiredAddress, e => e.StartIndex(1))
        .Build();

    private static readonly TabularMap<Holder> s_throwingHolder = TabularMap
        .For<Holder>()
        .Column(x => x.Name, index: 0)
        .Nested(
            x => x.Address,
            TabularMap
                .For<ThrowingAddress>()
                .Column(x => x.City, index: 0, key: ColumnKey.Create("city"), configure: c => c.ExportOnly())
                .Column(
                    x => x.PostalCode,
                    index: 1,
                    key: ColumnKey.Create("postalCode"),
                    configure: c => c.ExportOnly())
                .Build(),
            e => e.StartIndex(1))
        .Build();

    private static readonly CityOwner[] s_cityOwners = [new("Ada", null), new("Bob", new OptionalCity(null))];

    private static readonly TabularMap<CityOwner> s_cityOwnerMap = TabularMap
        .For<CityOwner>()
        .Column(x => x.Name, index: 0)
        .Nested(
            x => x.Address,
            TabularMap
                .For<OptionalCity>()
                .Column(x => x.City, index: 0, configure: c => c.NullText("NULL"))
                .Build(),
            e => e.StartIndex(1))
        .Build();

    [Fact]
    public async Task StartIndex_ShiftsEveryField()
    {
        // Act
        var report =
            await CsvInstance.ImportAsync(TestInput.Utf8(string.Join(",", s_sixteenFields)), s_shiftedOrder, Token);

        // Assert
        Assert.Equal(
            new Order("v0", new AddressDto("v5", "v6", "v8"), new AddressDto("v12", "v13", "v15")),
            Assert.Single(report.Items!));
    }

    [Fact]
    public async Task StartIndex_SetsTheMinimumWidth()
    {
        // Act
        var report = await CsvInstance.ImportAsync(
            TestInput.Utf8(string.Join(",", s_sixteenFields[..15])),
            s_shiftedOrder,
            Token);

        // Assert
        Assert.Equal(
            15,
            Assert.Single(report.Errors)
                .Location.ColumnIndex);
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task NestedText_KeepsLeadingZerosOnExport(
        TabularType type
    )
    {
        // Act
        var (_, file) = await Export.ExportAsync(s_dresdenUsers, s_userWithAddress, type, Token);

        // Assert
        Assert.Equal(
            ["Ada", "Dresden", "01067", "", "Hauptstraße"],
            Cells(type, file!)[0]
                .Select(cell => cell ?? ""));
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task NestedText_KeepsLeadingZerosOnReimport(
        TabularType type
    )
    {
        // Act
        // WHY: The round trip of the own output is the checked contract, so export and reimport form one action.
        var (_, file) = await Export.ExportAsync(s_dresdenUsers, s_userWithAddress, type, Token);
        var report = await Import.ImportAsync(file!, type, s_userWithAddress, Token);

        // Assert
        Assert.Equal(s_dresdenUsers, report.Items);
    }

    [Fact]
    public async Task OptionalNested_AllEmpty_IsNull()
    {
        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8("Ada,,,,"), s_userWithRequiredAddress, Token);

        // Assert
        Assert.Null(
            Assert.Single(report.Items!)
                .Address);
    }

    [Fact]
    public async Task OptionalNested_WithOneFilledField_AppliesAllRules()
    {
        // Act
        var report = await CsvInstance.ImportAsync(
            TestInput.Utf8("Ada,,01067,,\nBob,Bonn,53111,,Markt"),
            s_userWithRequiredAddress,
            new CsvImportOptions { Strategy = ImportStrategy.Partial },
            Token);

        // Assert
        Assert.Equal(
            ["Address.City", "Address.Street"],
            report
                .Errors
                .Select(error => error.Location.PropertyPath!.Value)
                .Order());

        Assert.Equal(["Bob"], report.Items!.Select(item => item.Name));
    }

    [Fact]
    public async Task OptionalNested_WithOneFilledFieldAndNoRules_IsRead()
    {
        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8("Ada,,01067,,"), s_userWithAddress, Token);

        // Assert
        Assert.Equal(
            new AddressDto("", "01067", ""),
            Assert.Single(report.Items!)
                .Address);
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task NullNested_ExportsEmptyCellsWithoutReadingChildProperties(
        TabularType type
    )
    {
        // Act
        var (report, file) = await Export.ExportAsync(new[] { new Holder("Ada", null) }, s_throwingHolder, type, Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        Assert.Equal(
            ["Ada", "", ""],
            Cells(type, file!)[0]
                .Select(cell => cell ?? "")
                .Concat(["", ""])
                .Take(3));
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task NullNested_ExportsEmptySelectedCellsWithoutReadingChildProperties(
        TabularType type
    )
    {
        // Act
        var (_, file) = await Export.ExportAsync(
            new[] { new Holder("Ada", null) },
            s_throwingHolder,
            type,
            new TabularExportOptions { ColumnKeys = [ColumnKey.Create("postalCode"), ColumnKey.Create("Name")] },
            Token);

        // Assert
        Assert.Equal(
            ["", "Ada"],
            Cells(type, file!)[0]
                .Select(cell => cell ?? ""));
    }

    [Fact]
    public async Task OptionalNested_WithTheTrimOfTheBaseType_IsNullForBlankFields()
    {
        // Arrange
        var city = TabularMap
            .For<OptionalCity>()
            .Column(
                x => x.City,
                index: 0,
                configure: c => c.Convert<string, string?>(
                    text => text,
                    text => text ?? "",
                    configure: b => b
                        .Trim()
                        .Required()))
            .Build();

        var map = TabularMap
            .For<CityOwner>()
            .Column(x => x.Name, index: 0)
            .Nested(x => x.Address, city, e => e.StartIndex(1))
            .Build();

        // Act
        var report = await CsvInstance.ImportAsync(
            TestInput.Utf8("Ada,   "),
            map,
            new CsvImportOptions { TrimFields = false },
            Token);

        // Assert
        Assert.Equal(new CityOwner("Ada", null), Assert.Single(report.Items!));
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task NullNested_KeepsChildCellsEmptyAlsoWithANullMarker(
        TabularType type
    )
    {
        // Act
        var (report, file) = await Export.ExportAsync(s_cityOwners, s_cityOwnerMap, type, Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        Assert.Equal(
            ["", "NULL"],
            Cells(type, file!)
                .Select(row => row.Length > 1
                    ? row[1] ?? ""
                    : ""));
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task NullNested_AndANullMarkerOfAChild_ReadBackDistinctly(
        TabularType type
    )
    {
        // Act
        // WHY: The round trip of the own output is the checked contract, so export and reimport form one action.
        var (_, file) = await Export.ExportAsync(s_cityOwners, s_cityOwnerMap, type, Token);
        var report = await Import.ImportAsync(file!, type, s_cityOwnerMap, Token);

        // Assert
        Assert.Equal(s_cityOwners, report.Items);
    }

    private static TabularMapBuilder<Payment> PaymentBuilder(
        Func<long, string, Money> read
    ) => TabularMap
        .For<Payment>()
        .Column(
            x => x.Email,
            index: 0,
            key: ColumnKey.Create("email"),
            configure: c => c
                .Header("E-Mail")
                .Validate(email => email.Contains('@', StringComparison.Ordinal)))
        .Compose(
            x => x.Amount,
            c => c.Field<long>(index: 3, key: ColumnKey.Create("amountMinor"), configure: f => f.Header("Betrag")),
            c => c.Field<string>(index: 4, key: ColumnKey.Create("currency"), configure: f => f.Header("Währung")),
            read: read,
            write: money => ((long)(money.Amount * 100), money.Currency));

    public sealed record AddressDto(
        string City,
        string PostalCode,
        string Street
    );

    public sealed record User(
        string Name,
        AddressDto? Address
    );

    public sealed record Order(
        string Number,
        AddressDto Billing,
        AddressDto Shipping
    );

    public sealed record Located(
        string City,
        string PostalCode
    );

    public sealed record Shipment(
        string Id,
        Located Billing,
        Located Shipping
    );

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public sealed record Contact(
        string Email,
        DateTime? DateOfHire
    );

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public sealed record Person(string Name);

    public sealed record OrderExportRow(
        Located Billing,
        Located Shipping
    );

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public sealed record IdUser(
        int Id,
        string Name
    );

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public sealed record IdAddress(
        int UserId,
        string City
    );

    public sealed record Money(
        decimal Amount,
        string Currency
    );

    public sealed record Payment(
        string Email,
        Money Amount
    );

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public sealed record OptionalPayment(
        string Email,
        Money? Amount
    );

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public sealed record Taxed(Money Total);

    public sealed class UnsupportedCurrencyException : Exception
    {
        public UnsupportedCurrencyException()
            : base("currency XYZ") { }
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public sealed class ThrowingAddress
    {
        // WHY: An instance field keeps City and PostalCode instance properties, which the map binds.
        // ReSharper disable once ConvertToConstant.Local
        private readonly string _message = "must not be read";

        public string City => throw new InvalidOperationException(_message);

        public string PostalCode => throw new InvalidOperationException(_message);
    }

    public sealed record Holder(
        string Name,
        ThrowingAddress? Address
    );

    public sealed record OptionalCity(string? City);

    public sealed record CityOwner(
        string Name,
        OptionalCity? Address
    );
}
