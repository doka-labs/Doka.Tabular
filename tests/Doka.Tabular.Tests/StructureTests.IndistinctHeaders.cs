using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Tests;

/// <summary>
/// What an export writes reads back by header name: headers equal after trimming and ignoring case are a configuration
/// error, fixed ones at <c>Build()</c>, translated ones per call.
/// </summary>
public sealed partial class StructureTests
{
    private static readonly Dictionary<string, string> s_equalTranslations = new()
    {
        ["first"] = "Name",
        ["second"] = " name",
    };

    private static readonly TabularMap<Located> s_translatedHeaders = TabularMap
        .For<Located>()
        .Column(x => x.City, index: 0, configure: c => c.HeaderTranslationKey("first"))
        .Column(x => x.PostalCode, index: 1, configure: c => c.HeaderTranslationKey("second"))
        .Build();

    private static readonly TabularMap<Located> s_fixedLocated = TabularMap
        .For<Located>()
        .Column(x => x.City, index: 0, key: ColumnKey.Create("city"), configure: c => c.Header("City"))
        .Column(x => x.PostalCode, index: 1, key: ColumnKey.Create("postalCode"), configure: c => c.Header("PLZ"))
        .Build();

    private static readonly TabularMap<Located> s_cityOnly = TabularMap
        .For<Located>()
        .Column(x => x.City, index: 0, configure: c => c.Header("City"))
        .Build();

    [Theory]
    [InlineData("Name", "name")]
    [InlineData("Name", " Name")]
    [InlineData(" name ", "NAME")]
    public void FixedHeadersEqualAfterTrimOrCase_AreAConfigurationErrorAtBuild(
        string first,
        string second
    )
    {
        // Act
        var exception = Assert.Throws<TabularConfigurationException>(() => TabularMap
            .For<Located>()
            .Column(x => x.City, index: 0, configure: c => c.Header(first))
            .Column(x => x.PostalCode, index: 1, configure: c => c.Header(second))
            .Build());

        // Assert
        Assert.Contains(exception.Errors, error => error.Message.Contains("equal after trimming"));
    }

    [Theory]
    [InlineData("distinct")]
    [InlineData("import-only twin")]
    [InlineData("without header")]
    public void DistinctHeaders_ImportOnlyTwins_AndFieldsWithoutHeader_Build(
        string kind
    )
    {
        // Act
        // WHY: Only written headers must be distinct; an import-only field may read the column another field writes.
        var exception = Record.Exception(() => kind switch
        {
            "distinct" => TabularMap
                .For<Located>()
                .Column(x => x.City, index: 0, configure: c => c.Header("Name"))
                .Column(x => x.PostalCode, index: 1, configure: c => c.Header("Name 2"))
                .Build(),
            "import-only twin" => TabularMap
                .For<Located>()
                .Column(x => x.City, index: 0, configure: c => c.Header("Name"))
                .Column(
                    x => x.PostalCode,
                    index: 1,
                    configure: c => c
                        .Header("name")
                        .ImportOnly())
                .Build(),
            _ => TabularMap
                .For<Located>()
                .Column(x => x.City, index: 0)
                .Column(x => x.PostalCode, index: 1)
                .Build(),
        });

        // Assert
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("Rechnung ", "RECHNUNG ")]
    public void EmbeddingsWithEqualHeaders_AreAConfigurationErrorAtBuild(
        string? billingPrefix,
        string? shippingPrefix
    )
    {
        // Act
        var exception = Record.Exception(() => OrderWithPrefixes(billingPrefix, shippingPrefix));

        // Assert
        Assert.Contains(
            Assert
                .IsType<TabularConfigurationException>(exception)
                .Errors,
            error => error.Message.Contains("equal after trimming"));
    }

    [Fact]
    public void EmbeddingsWithDistinctHeaderPrefixes_Build()
    {
        // Act
        var exception = Record.Exception(() => OrderWithPrefixes("Rechnung ", "Lieferung "));

        // Assert
        Assert.Null(exception);
    }

    private static TabularMap<Order> OrderWithPrefixes(
        string? billingPrefix,
        string? shippingPrefix
    ) => TabularMap
        .For<Order>()
        .Column(x => x.Number, index: 0, configure: c => c.Header("Nummer"))
        .Nested(
            x => x.Billing,
            s_address,
            e =>
            {
                e
                    .StartIndex(1)
                    .KeyPrefix(ColumnKey.Create("billing"));

                if (billingPrefix is not null)
                {
                    e.HeaderPrefix(billingPrefix);
                }
            })
        .Nested(
            x => x.Shipping,
            s_address,
            e =>
            {
                e
                    .StartIndex(5)
                    .KeyPrefix(ColumnKey.Create("shipping"));

                if (shippingPrefix is not null)
                {
                    e.HeaderPrefix(shippingPrefix);
                }
            })
        .Build();

    [Theory]
    [InlineData("Adresse ", "adresse ")]
    [InlineData("Adresse", " ADRESSE")]
    public void RowMapRolesWithEqualFixedHeaders_AreAConfigurationErrorAtBuild(
        string first,
        string second
    )
    {
        // Arrange
        var rows = RowMap.Create();
        rows.Add(
            s_fixedLocated,
            e => e
                .KeyPrefix(ColumnKey.Create("billing"))
                .HeaderPrefix(first));

        // Act
        rows.Add(
            s_fixedLocated,
            e => e
                .StartIndex(2)
                .KeyPrefix(ColumnKey.Create("shipping"))
                .HeaderPrefix(second));

        // Assert
        Assert.Contains(
            Assert
                .Throws<TabularConfigurationException>(rows.Build)
                .Errors,
            error => error.Message.Contains("equal after trimming"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RowMapOverridesOfFieldsTheRoleMapDoesNotMap_AreAConfigurationErrorAtBuild(
        bool header
    )
    {
        // Arrange
        var rowMap = RowMap.Create();
        if (header)
        {
            rowMap.Add(s_cityOnly, e => e.Header(x => x.PostalCode, "PLZ"));
        }
        else
        {
            rowMap.Add(s_cityOnly, e => e.ExportKey(x => x.PostalCode, ColumnKey.Create("zip")));
        }

        // Act
        var exception = Record.Exception(rowMap.Build);

        // Assert
        Assert.Contains(
            Assert
                .IsType<TabularConfigurationException>(exception)
                .Errors,
            error => error.Message.Contains("PostalCode"));
    }

    [Fact]
    public void RowMapOverridesOfMappedFields_Build()
    {
        // Arrange
        var rowMap = RowMap.Create();
        rowMap.Add(
            s_cityOnly,
            e => e
                .ExportKey(x => x.City, ColumnKey.Create("town"))
                .Header(x => x.City, "Ort"));

        // Act
        var exception = Record.Exception(rowMap.Build);

        // Assert
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RowMapRolesWithDistinctOrImportOnlyHeaders_Build(
        bool importOnly
    )
    {
        // Arrange
        var rowMap = RowMap.Create();
        if (importOnly)
        {
            rowMap.Add(s_fixedLocated, e => e.KeyPrefix(ColumnKey.Create("billing")));
            rowMap.Add(
                s_fixedLocated,
                e => e
                    .StartIndex(2)
                    .KeyPrefix(ColumnKey.Create("shipping"))
                    .ImportOnly());
        }
        else
        {
            rowMap.Add(
                s_fixedLocated,
                e => e
                    .KeyPrefix(ColumnKey.Create("billing"))
                    .HeaderPrefix("Rechnung "));

            rowMap.Add(
                s_fixedLocated,
                e => e
                    .StartIndex(2)
                    .KeyPrefix(ColumnKey.Create("shipping"))
                    .HeaderPrefix("Lieferung "));
        }

        // Act
        var exception = Record.Exception(rowMap.Build);

        // Assert
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("csv")]
    [InlineData("excel")]
    [InlineData("workbook")]
    [InlineData("common")]
    public async Task TranslatedHeadersEqualAfterTrimOrCase_AreAConfigurationReportOnEveryExportEntryPoint(
        string entry
    )
    {
        // Arrange
        var rows = new[] { new Located("Bern", "3000") };
        var sheets = ExcelExport.Create();
        sheets.Add(
            TabularSheetName.Create("Sheet1"),
            rows,
            s_translatedHeaders,
            new ExcelSheetExportOptions { Translations = s_equalTranslations });

        // Act
        var errors = entry switch
        {
            "csv" => (await CsvInstance.ExportAsync(
                rows,
                s_translatedHeaders,
                new MemoryStream(),
                new CsvExportOptions
                {
                    WriteHeader = true,
                    Translations = s_equalTranslations,
                },
                Token)).Errors,
            "excel" => (await ExcelInstance.Format.ExportAsync(
                rows,
                s_translatedHeaders,
                new MemoryStream(),
                new ExcelExportOptions
                {
                    WriteHeader = true,
                    Translations = s_equalTranslations,
                },
                Token)).Errors,
            "workbook" => (await ExcelInstance.Format.ExportAsync(
                sheets.Build(),
                new MemoryStream(),
                new ExcelWorkbookExportOptions { WriteHeader = true },
                Token)).Errors,
            _ => (await CommonExportWithEqualHeadersAsync()).Report.Errors,
        };

        // Assert
        Assert.Contains(
            "equal after trimming",
            Assert.Single(errors)
                .Message);
    }

    [Fact]
    public async Task TranslatedHeadersEqualAfterTrimOrCase_ReturnNoFileFromTheCommonExport()
    {
        // Act
        var (_, file) = await CommonExportWithEqualHeadersAsync();

        // Assert
        Assert.Null(file);
    }

    private static Task<BufferedExportResult> CommonExportWithEqualHeadersAsync() => Export.ExportAsync(
        new[] { new Located("Bern", "3000") },
        s_translatedHeaders,
        TabularType.Csv,
        new TabularExportOptions
        {
            WriteHeader = true,
            Translations = s_equalTranslations,
        },
        Token);

    [Fact]
    public async Task DistinctTranslatedHeaders_Export()
    {
        // Act
        var report = await CsvInstance.ExportAsync(
            new[] { new Located("Bern", "3000") },
            s_translatedHeaders,
            new MemoryStream(),
            new CsvExportOptions
            {
                WriteHeader = true,
                Translations = new Dictionary<string, string>
                {
                    ["first"] = "Ort",
                    ["second"] = "PLZ",
                },
            },
            Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
    }

    [Fact]
    public async Task EqualTranslatedHeaders_ExportWithoutHeader()
    {
        // Act
        var report = await CsvInstance.ExportAsync(
            new[] { new Located("Bern", "3000") },
            s_translatedHeaders,
            new MemoryStream(),
            new CsvExportOptions
            {
                WriteHeader = false,
                Translations = s_equalTranslations,
            },
            Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
    }

    [Fact]
    public async Task TranslatedHeaderPrefixesMakingHeadersEqual_AreAConfigurationReport()
    {
        // Arrange
        var map = TabularMap
            .For<Shipment>()
            .Column(x => x.Id, index: 0, configure: c => c.Header("Id"))
            .Nested(
                x => x.Billing,
                s_located,
                e => e
                    .StartIndex(1)
                    .KeyPrefix(ColumnKey.Create("billing"))
                    .HeaderPrefixTranslationKey("billing"))
            .Nested(
                x => x.Shipping,
                s_located,
                e => e
                    .StartIndex(3)
                    .KeyPrefix(ColumnKey.Create("shipping"))
                    .HeaderPrefixTranslationKey("shipping"))
            .Build();

        // Act
        var report = await CsvInstance.ExportAsync(
            new[] { new Shipment("1", new Located("A", "1"), new Located("B", "2")) },
            map,
            new MemoryStream(),
            new CsvExportOptions
            {
                WriteHeader = true,
                Translations = new Dictionary<string, string>
                {
                    ["billing"] = "Adresse ",
                    ["shipping"] = "adresse ",
                    ["ort"] = "Ort",
                    ["plz"] = "PLZ",
                },
            },
            Token);

        // Assert
        // WHY: One error per indistinct pair: the city columns and the postal code columns of both addresses.
        Assert.Equal(2, report.Errors.Count);
        Assert.All(report.Errors, error => Assert.Contains("equal after trimming", error.Message));
    }
}
