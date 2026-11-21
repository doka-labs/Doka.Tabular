using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Tests;

public sealed partial class BehaviorTests
{
    private static readonly TabularMap<Contact> s_contacts = TabularMap
        .For<Contact>()
        .Column(x => x.FirstName, index: 0)
        .Column(x => x.LastName, index: 1)
        .Column(x => x.FullName, index: 2, key: ColumnKey.Create("fullName"), configure: c => c.ExportOnly())
        .Build();

    private static readonly TabularMap<Sourced> s_sourced = TabularMap
        .For<Sourced>()
        .Column(x => x.FirstName, index: 0)
        .Column(x => x.SourceReference,
            index: 1,
            key: ColumnKey.Create("sourceReference"),
            configure: c => c
                .ImportOnly()
                .Required())
        .Column(x => x.LastName, index: 2)
        .Build();

    private static readonly TabularMap<MailCity> s_mailCities = TabularMap
        .For<MailCity>()
        .Column(x => x.Email, index: 0)
        .Column(x => x.City, index: 1)
        .Build();

    private static readonly TabularMap<Employee> s_emailColumn = TabularMap
        .For<Employee>()
        .Column(x => x.Email, index: 0)
        .Build();

    private static Sourced[] SourcedRows => [new("Ada", "EXT-42", "Lovelace")];

    private static List<Employee> OneEmployee => [new("a", 1, "", "")];

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task ExportOnlyColumn_IsIgnoredOnImport(
        TabularType type
    )
    {
        // Act
        var report = await Import.ImportAsync(Create(type, ["Ada", "Lovelace", "ignored"]), type, s_contacts, Token);

        // Assert
        Assert.Equal(new Contact("Ada", "Lovelace"), Assert.Single(report.Items!));
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task ExportOnlyColumn_IsWrittenOnExport(
        TabularType type
    )
    {
        // Act
        var (_, file) = await Export.ExportAsync(new[] { new Contact("Ada", "Lovelace") },
            s_contacts,
            type,
            new TabularExportOptions { ColumnKeys = [ColumnKey.Create("fullName")] },
            Token);

        // Assert
        Assert.Equal([["Ada Lovelace"]], Cells(type, file!));
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task ImportOnlyColumn_IsReadOnImport(
        TabularType type
    )
    {
        // Act
        var report = await Import.ImportAsync(Create(type, ["Ada", "EXT-42", "Lovelace"]), type, s_sourced, Token);

        // Assert
        Assert.Equal("EXT-42",
            Assert.Single(report.Items!)
                .SourceReference);
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task ImportOnlyColumn_StaysEmptyOnExport(
        TabularType type
    )
    {
        // Act
        var (_, file) = await Export.ExportAsync(SourcedRows, s_sourced, type, Token);

        // Assert
        Assert.Equal(["Ada", "", "Lovelace"],
            Cells(type, file!)[0]
                .Select(cell => cell ?? ""));
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task ImportOnlyColumn_SelectedForExport_IsAConfigurationErrorBeforeWriting(
        TabularType type
    )
    {
        // Arrange
        var target = new MemoryStream();

        // Act
        var report = await Export.ExportAsync(SourcedRows,
            s_sourced,
            target,
            type,
            new TabularExportOptions { ColumnKeys = [ColumnKey.Create("sourceReference")] },
            Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Configuration.Invalid, (report.FatalError ?? report.Errors[0]).Code);
        Assert.Equal(0, target.Length);
    }

    [Theory]
    [InlineData("email,postCode")]
    [InlineData("email,email")]
    public async Task ColumnKeys_InvalidSelectionsFailWithoutWriting(
        string keys
    )
    {
        // Arrange
        var target = new MemoryStream();
        IReadOnlyList<ColumnKey> selection =
        [
            .. keys
                .Split(',')
                .Select(ColumnKey.Create),
        ];

        // Act
        var report = await CsvInstance.ExportAsync(new[] { new Employee("a@example.org", 1, "Berlin", "10115") },
            s_employees,
            target,
            new CsvExportOptions { ColumnKeys = selection },
            Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Configuration.Invalid, (report.FatalError ?? report.Errors[0]).Code);
        Assert.Equal(0, target.Length);
    }

    [Theory]
    [InlineData(false, "Postal code;Email")]
    [InlineData(true, "Postleitzahl;E-Mail")]
    public async Task ColumnKeys_AreCaseInsensitiveAndKeepTheRequestedOrder(
        bool german,
        string header
    )
    {
        // Arrange
        var translations = german
            ? new Dictionary<string, string>
            {
                ["postal"] = "Postleitzahl",
                ["mail"] = "E-Mail",
            }
            : new Dictionary<string, string>
            {
                ["postal"] = "Postal code",
                ["mail"] = "Email",
            };

        var map = TabularMap
            .For<Employee>()
            .Column(x => x.Email,
                index: 0,
                key: ColumnKey.Create("email"),
                configure: c => c
                    .Header("Mail")
                    .HeaderTranslationKey("mail"))
            .Column(x => x.PostalCode,
                index: 3,
                key: ColumnKey.Create("postalCode"),
                configure: c => c
                    .Header("PLZ")
                    .HeaderTranslationKey("postal"))
            .Build();

        // Act
        var (_, file) = await CsvInstance.ExportAsync(
            new[] { new Employee("ada@example.org", 1, "Halle; Saale", "01067") },
            map,
            new CsvExportOptions
            {
                ColumnKeys = [ColumnKey.Create("PostalCode"), ColumnKey.Create("email")],
                Translations = translations,
                WriteHeader = true,
                Delimiter = ';',
            },
            Token);

        // Assert
        Assert.Equal(header + "\r\n01067;ada@example.org", TestInput.Utf8Text(file!));
    }

    [Fact]
    public async Task SemicolonInText_IsQuoted()
    {
        // Act
        var (_, file) = await CsvInstance.ExportAsync(new[] { new MailCity("a", "Halle; Saale") },
            s_mailCities,
            new CsvExportOptions { Delimiter = ';' },
            Token);

        // Assert
        Assert.Equal("a;\"Halle; Saale\"", TestInput.Utf8Text(file!));
    }

    [Fact]
    public async Task SemicolonInText_ReadsBack()
    {
        // Act
        // WHY: The round trip of the own output is the checked contract, so export and reimport form one action.
        var (_, file) = await CsvInstance.ExportAsync(new[] { new MailCity("a", "Halle; Saale") },
            s_mailCities,
            new CsvExportOptions { Delimiter = ';' },
            Token);

        var report =
            await CsvInstance.ImportAsync(file!, s_mailCities, new CsvImportOptions { Delimiter = ';' }, Token);

        // Assert
        Assert.Equal("Halle; Saale",
            Assert.Single(report.Items!)
                .City);
    }

    [Fact]
    public void ColumnKeys_CollidingAcrossInclusions_FailAtBuild()
    {
        // Act
        var address = TabularMap
            .For<Employee>()
            .Column(x => x.PostalCode, index: 0, key: ColumnKey.Create("postalCode"))
            .Build();

        // Assert
        Assert.Throws<TabularConfigurationException>(() => RowMapWith(address));

        return;

        static RowMap RowMapWith(
            TabularMap<Employee> map
        )
        {
            var rows = RowMap.Create();
            rows.Add(map, e => e.KeyPrefix(ColumnKey.Create("billing")));
            rows.Add(map,
                e => e
                    .StartIndex(1)
                    .ExportKey(x => x.PostalCode, ColumnKey.Create("BILLING.POSTALCODE")));

            return rows.Build();
        }
    }

    [Fact]
    public async Task Masking_KeepsTheChosenCharactersAndRequiresExportOnly()
    {
        // Arrange
        var map = TabularMap
            .For<Account>()
            .Column(x => x.MaskedIban,
                index: 0,
                configure: c => c
                    .ExportOnly()
                    .MaskExceptLast(visibleCount: 4, maskChar: '*'))
            .Column(x => x.MaskedName,
                index: 1,
                configure: c => c
                    .ExportOnly()
                    .MaskExceptFirst(visibleCount: 2, maskChar: '*'))
            .Build();

        var rows = new[] { new Account("DE89370400440532013000", "Lovelace"), new Account("123", "") };

        // Act
        var (_, file) = await CsvInstance.ExportAsync(rows, map, Token);

        // Assert
        Assert.Equal([["******************3000", "Lo******"], ["***", ""]], Cells(TabularType.Csv, file!));
        Assert.Throws<TabularConfigurationException>(() => TabularMap
            .For<Account>()
            .Column(x => x.Iban, index: 0, configure: c => c.MaskExceptLast(visibleCount: 4, maskChar: '*'))
            .Build());

        Assert.Throws<TabularConfigurationException>(() => TabularMap
            .For<Account>()
            .Column(x => x.Iban,
                index: 0,
                configure: c => c.Convert<string, string>(value => value,
                    value => value,
                    configure: b => b.MaskExceptLast(visibleCount: 1, maskChar: '*')))
            .Build());
    }

    [Fact]
    public async Task Sources_AcceptAnEnumerable()
    {
        // Act
        var report = await CsvInstance.ExportAsync(OneEmployee, s_emailColumn, new MemoryStream(), Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
    }

    [Fact]
    public async Task Sources_AcceptAnAsyncSequence()
    {
        // Act
        var report = await CsvInstance.ExportAsync(Async(OneEmployee), s_emailColumn, new MemoryStream(), Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);

        return;

        static async IAsyncEnumerable<Employee> Async(
            IEnumerable<Employee> rows
        )
        {
            foreach (var row in rows)
            {
                await Task.Yield();

                yield return row;
            }
        }
    }

    [Fact]
    public async Task Sources_RejectASynchronousQueryBeforeWriting()
    {
        // Arrange
        var target = new MemoryStream();

        // Act
        var report = await CsvInstance.ExportAsync(OneEmployee.AsQueryable(), s_emailColumn, target, Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Configuration.Invalid, (report.FatalError ?? report.Errors[0]).Code);
        Assert.Equal(0, target.Length);
    }
}
