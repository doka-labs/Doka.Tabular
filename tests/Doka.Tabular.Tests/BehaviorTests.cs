using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Tests;

/// <summary>Translations, validation, the error object, reports, column selection, the common entry point, error codes, and diagnostics.</summary>
public sealed partial class BehaviorTests
{
    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public enum Status
    {
        Active = 1,
        Approved = 2,
        Suspended = 3,
    }

    private static readonly TabularMap<Place> s_places = TabularMap
        .For<Place>()
        .Column(x => x.City,
            index: 0,
            key: ColumnKey.Create("city"),
            configure: c => c
                .Header("Ort")
                .HeaderTranslationKey("city"))
        .Column(x => x.State,
            index: 1,
            key: ColumnKey.Create("status"),
            configure: c => c
                .Header("Status")
                .TranslationKeys(value => $"status.{value}"))
        .Build();

    private static readonly Dictionary<string, string> s_english = new()
    {
        ["city"] = "City",
        ["status.Active"] = "Active",
        ["status.Approved"] = "Approved",
        ["status.Suspended"] = "Suspended",
    };

    private static readonly TabularMap<Employee> s_employees = TabularMap
        .For<Employee>()
        .Column(x => x.Email, index: 0, key: ColumnKey.Create("email"), configure: c => c.Header("E-Mail"))
        .Column(x => x.NumericId,
            index: 1,
            configure: c => c
                .Header("Nummer")
                .IncludeSourceValue()
                .Validate(number => number >= 1,
                    code: TabularErrorCode.Create("personnelNumber.mustBePositive"),
                    alsoOnExport: true))
        .Column(x => x.City, index: 2, key: ColumnKey.Create("city"), configure: c => c.Header("Ort"))
        .Column(x => x.PostalCode,
            index: 3,
            key: ColumnKey.Create("postalCode"),
            configure: c => c
                .Header("PLZ")
                .Validate(code => code.Length == 5, alsoOnExport: true))
        .Validate(x => x.City,
            x => x.PostalCode,
            (city, postal) => city.Length == 0 || postal.Length > 0,
            code: TabularErrorCode.Create("city.needsPostalCode"),
            alsoOnExport: true)
        .Build();

    private static readonly CsvFormat s_semicolonCsv =
        new(new CsvFormatOptions { Export = new CsvExportOptions { Delimiter = ';' } });

    private static readonly TabularMap<Pair> s_pairs = TabularMap
        .For<Pair>()
        .Column(x => x.Email, index: 0)
        .Column(x => x.NumericId, index: 1)
        .Build();

    private static readonly TabularImportOptions s_strictExcel = new()
    {
        Strategy = ImportStrategy.Partial,
        Excel = new ExcelImportOptions { Strategy = ImportStrategy.Strict },
    };

    private static readonly TabularMap<Employee> s_emailOnly = TabularMap
        .For<Employee>()
        .Column(x => x.Email, index: 0)
        .Build();

    [Fact]
    public async Task CommonEntry_UsesTheInstanceDefaultsOfTheFormat()
    {
        // Act
        var (_, file) =
            await new TabularExport(s_semicolonCsv).ExportAsync(new[] { new Pair("a", 1) },
                s_pairs,
                TabularType.Csv,
                Token);

        // Assert
        Assert.Equal("a;1", TestInput.Utf8Text(file!));
    }

    [Fact]
    public async Task CommonEntry_CallOptionsOverrideTheInstanceDefaults()
    {
        // Act
        var (_, file) = await new TabularExport(s_semicolonCsv).ExportAsync(new[] { new Pair("a", 1) },
            s_pairs,
            TabularType.Csv,
            new TabularExportOptions { Csv = new CsvExportOptions { Delimiter = ',' } },
            Token);

        // Assert
        Assert.Equal("a,1", TestInput.Utf8Text(file!));
    }

    [Fact]
    public async Task CommonEntry_WithoutTheRequestedFormat_IsAConfigurationErrorBeforeWriting()
    {
        // Arrange
        var target = new MemoryStream();

        // Act
        var report = await new TabularExport(s_semicolonCsv).ExportAsync(new[] { new Pair("a", 1) },
            s_pairs,
            target,
            TabularType.Excel,
            Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Configuration.Invalid, (report.FatalError ?? report.Errors[0]).Code);
        Assert.Equal(0, target.Length);
    }

    [Fact]
    public async Task CommonEntry_WorkbookReadAsCsv_IsAFormatMismatch()
    {
        // Arrange
        var (_, workbook) = await ExcelInstance.ExportAsync(new[] { new Pair("a", 1) }, s_pairs, Token);

        // Act
        var report =
            await new TabularImport(s_semicolonCsv, ExcelInstance.Format).ImportAsync(workbook!,
                TabularType.Csv,
                s_pairs,
                Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Schema.FormatMismatch,
            Assert.Single(report.Errors)
                .Code);
    }

    [Fact]
    public async Task CommonEntry_CommonStrategyAppliesToAFormatWithoutItsOwn()
    {
        // Act
        var report = await new TabularImport(s_semicolonCsv, ExcelInstance.Format).ImportAsync(
            TestInput.Utf8("a,1\nb,x"),
            TabularType.Csv,
            s_pairs,
            s_strictExcel,
            Token);

        // Assert
        Assert.Equal(ReportStatus.CompletedWithErrors, report.Status);
    }

    [Fact]
    public async Task CommonEntry_StrategyOfTheFormatOptionsTakesPrecedence()
    {
        // Act
        var report = await new TabularImport(s_semicolonCsv, ExcelInstance.Format).ImportAsync(
            Create(TabularType.Excel, ["a", 1], ["b", "x"]),
            TabularType.Excel,
            s_pairs,
            s_strictExcel,
            Token);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    [Fact]
    public async Task CommonImport_UndefinedTypeThrowsBeforeAccess()
    {
        // Act
        var exception = await Record.ExceptionAsync(()
            => Import.ImportAsync(TestInput.Utf8("a"), (TabularType)7, s_emailOnly, Token));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    [Fact]
    public async Task CommonExport_UndefinedTypeThrowsBeforeAccess()
    {
        // Act
        var exception = await Record.ExceptionAsync(()
            => Export.ExportAsync(Array.Empty<Employee>(), s_emailOnly, (TabularType)7, Token));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    [Theory]
    [InlineData(TabularType.Csv, "text/csv", ".csv")]
    [InlineData(TabularType.Excel, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ".xlsx")]
    public void TabularType_ProvidesContentTypeAndExtension(
        TabularType type,
        string contentType,
        string extension
    )
    {
        // Act
        var actual = (type.ContentType, type.FileExtension);

        // Assert
        Assert.Equal((contentType, extension), actual);
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task ContinuousExample_PartialImportKeepsTheValidArticles(
        TabularType type
    )
    {
        // Act
        var report = await Import.ImportAsync(Create(type, ["A-01", 12], ["A-02", "unbekannt"], ["A-03", 0]),
            type,
            s_pairs,
            new TabularImportOptions { Strategy = ImportStrategy.Partial },
            Token);

        // Assert
        Assert.Equal([("A-01", 12), ("A-03", 0)], report.Items!.Select(item => (item.Email, item.NumericId)));
        var error = Assert.Single(report.Errors);
        Assert.Equal((2L, 1, "NumericId"),
            (error.Location.RecordNumber!.Value, error.Location.ColumnIndex!.Value,
                error.Location.PropertyPath!.Value));

        Assert.Equal(type == TabularType.Excel, error.Location.SheetName is not null);
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task ContinuousExample_ExportedArticlesReadBack(
        TabularType type
    )
    {
        // Arrange
        Pair[] articles = [new("A-01", 12), new("A-03", 0)];

        // Act
        // WHY: The round trip of the own output is the checked contract, so export and reimport form one action.
        var (_, file) = await Export.ExportAsync(articles, s_pairs, type, Token);
        var report = await Import.ImportAsync(file!, type, s_pairs, Token);

        // Assert
        Assert.Equal(articles, report.Items);
    }

    public sealed record Place(
        string City,
        Status State
    );

    public sealed record Employee(
        string Email,
        int NumericId,
        string City,
        string PostalCode
    );

    public sealed record Contact(
        string FirstName,
        string LastName
    )
    {
        public string FullName => $"{FirstName} {LastName}";
    }

    public sealed record Pair(
        string Email,
        int NumericId
    );

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public sealed record Mail(string Email);

    public sealed record MailCity(
        string Email,
        string City
    );

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public sealed record Triple(
        string Email,
        int NumericId,
        string City
    );

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public sealed record Sourced(
        string FirstName,
        string SourceReference,
        string LastName
    );

    public sealed record Account(
        string Iban,
        string Name
    )
    {
        public string MaskedIban => Iban;

        public string MaskedName => Name;
    }
}
