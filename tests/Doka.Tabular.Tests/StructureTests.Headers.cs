using System.Text.RegularExpressions;
using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Tests;

public sealed partial class StructureTests
{
    private static readonly Shipment[] s_shipments =
        [new("1", new Located("Dresden", "01067"), new Located("Bonn", "53111"))];

    private static readonly Dictionary<string, string> s_englishPrefix = new()
    {
        ["ort"] = "City",
        ["plz"] = "Postal code",
        ["shippingPrefix"] = "Shipping ",
    };

    private static readonly TabularMap<Person> s_postalCode = TabularMap
        .For<Person>()
        .Column(x => x.Name, index: 0, configure: c => c.Header("Postal Code"))
        .Build();

    private static readonly CsvImportOptions s_withoutWhitespace = new()
    {
        Binding = ColumnBinding.ByHeaderName,
        NormalizeHeader = header => Whitespace()
            .Replace(header, ""),
    };

    private static readonly TabularMap<Contact> s_optionalHire = TabularMap
        .For<Contact>()
        .Column(x => x.Email, index: 0, configure: c => c.Header("E-Mail"))
        .Column(
            x => x.DateOfHire,
            index: 1,
            configure: c => c
                .Header("Eintrittsdatum")
                .OptionalHeader())
        .Build();

    // WHY: A property, because the field it uses is declared in another part of the class.
    private static TabularMap<Shipment> PrefixedShipments => TabularMap
        .For<Shipment>()
        .Column(x => x.Id, index: 0, configure: c => c.Header("Id"))
        .Nested(
            x => x.Billing,
            s_located,
            e => e
                .StartIndex(1)
                .KeyPrefix(ColumnKey.Create("billing"))
                .HeaderPrefix("Rechnungs-"))
        .Nested(
            x => x.Shipping,
            s_located,
            e => e
                .StartIndex(3)
                .KeyPrefix(ColumnKey.Create("shipping"))
                .HeaderPrefix("Liefer-")
                .Header(a => a.PostalCode, "Zielpostleitzahl"))
        .Build();

    [Fact]
    public async Task HeaderPrefixes_AndFieldHeaders_DriveTheExport()
    {
        // Act
        var (_, file) = await CsvInstance.ExportAsync(
            s_shipments,
            PrefixedShipments,
            new CsvExportOptions
            {
                WriteHeader = true,
                Delimiter = ';',
            },
            Token);

        // Assert
        Assert.Equal(
            ["Id", "Rechnungs-Ort", "Rechnungs-PLZ", "Liefer-Ort", "Zielpostleitzahl"],
            Cells(TabularType.Csv, file!, delimiter: ';')[0]);
    }

    [Fact]
    public async Task HeaderPrefixes_AndFieldHeaders_DriveTheHeaderNameImport()
    {
        // Arrange
        const string reordered =
            "Liefer-Ort;Zielpostleitzahl;Id;Rechnungs-PLZ;Rechnungs-Ort\nBonn;53111;1;01067;Dresden";

        // Act
        var report = await CsvInstance.ImportAsync(
            TestInput.Utf8(reordered),
            PrefixedShipments,
            new CsvImportOptions
            {
                Binding = ColumnBinding.ByHeaderName,
                Delimiter = ';',
            },
            Token);

        // Assert
        Assert.Equal(s_shipments, report.Items);
    }

    [Fact]
    public async Task TranslatedPrefix_DrivesTheExportedHeader()
    {
        // Act
        var (_, file) = await CsvInstance.ExportAsync(
            s_shipments,
            TranslatedPrefixMap(),
            new CsvExportOptions
            {
                WriteHeader = true,
                Translations = s_englishPrefix,
            },
            Token);

        // Assert
        Assert.StartsWith(
            "Id,Rechnungs-City,Rechnungs-Postal code,Shipping City,Shipping Postal code",
            TestInput.Utf8Text(file!),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TranslatedPrefix_BindsWithTheSameTranslations()
    {
        // Arrange
        var map = TranslatedPrefixMap();
        var (_, file) = await CsvInstance.ExportAsync(
            s_shipments,
            map,
            new CsvExportOptions
            {
                WriteHeader = true,
                Translations = s_englishPrefix,
            },
            Token);

        var text = TestInput.Utf8Text(file!);

        // Act
        var report = await ImportByHeaderAsync(text, map, s_englishPrefix);

        // Assert
        Assert.Equal(s_shipments, report.Items);
    }

    [Fact]
    public async Task TranslatedPrefix_DoesNotBindWithOtherTranslations()
    {
        // Arrange
        var map = TranslatedPrefixMap();
        var (_, file) = await CsvInstance.ExportAsync(
            s_shipments,
            map,
            new CsvExportOptions
            {
                WriteHeader = true,
                Translations = s_englishPrefix,
            },
            Token);

        var text = TestInput.Utf8Text(file!);
        var german = new Dictionary<string, string>
        {
            ["ort"] = "Ort",
            ["plz"] = "PLZ",
            ["shippingPrefix"] = "Liefer-",
        };

        // Act
        var report = await ImportByHeaderAsync(text, map, german);

        // Assert
        Assert.Equal(TabularErrorCodes.Schema.MissingColumn, report.Errors[0].Code);
    }

    [Fact]
    public async Task TranslatedPrefix_OfAnUnwrittenHeader_NeedsNoTranslation()
    {
        // Act
        var report = await CsvInstance.ExportAsync(
            s_shipments,
            TranslatedPrefixMap(),
            new MemoryStream(),
            new CsvExportOptions { ColumnKeys = [ColumnKey.Create("shipping.postalCode")] },
            Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
    }

    /// <summary>Billing with a fixed header prefix, shipping with a translated one.</summary>
    private static TabularMap<Shipment> TranslatedPrefixMap() => TabularMap
        .For<Shipment>()
        .Column(x => x.Id, index: 0, configure: c => c.Header("Id"))
        .Nested(
            x => x.Billing,
            s_located,
            e => e
                .StartIndex(1)
                .KeyPrefix(ColumnKey.Create("billing"))
                .HeaderPrefix("Rechnungs-"))
        .Nested(
            x => x.Shipping,
            s_located,
            e => e
                .StartIndex(3)
                .KeyPrefix(ColumnKey.Create("shipping"))
                .HeaderPrefix("Liefer-")
                .HeaderPrefixTranslationKey("shippingPrefix"))
        .Build();

    private static Task<ImportReport<Shipment>> ImportByHeaderAsync(
        string text,
        TabularMap<Shipment> map,
        IReadOnlyDictionary<string, string> translations
    ) => CsvInstance.ImportAsync(
        TestInput.Utf8(text),
        map,
        new CsvImportOptions
        {
            Binding = ColumnBinding.ByHeaderName,
            Translations = translations,
        },
        Token);

    public static TheoryData<string?, string?, TabularType> EmptyHeaders()
    {
        var data = new TheoryData<string?, string?, TabularType>();
        foreach (var type in new[] { TabularType.Csv, TabularType.Excel })
        {
            data.Add("", null, type);
            data.Add(" ", null, type);
            data.Add(null, " ", type);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EmptyHeaders))]
    public async Task EmptyHeader_CannotBeBoundByNameInEitherFormat(
        string? header,
        string? translation,
        TabularType type
    )
    {
        // Arrange
        var map = TabularMap
            .For<Person>()
            .Column(
                x => x.Name,
                index: 0,
                configure: c =>
                {
                    if (header is not null)
                    {
                        c.Header(header);
                    }
                    else
                    {
                        c.HeaderTranslationKey("name");
                    }
                })
            .Build();

        // Act
        var report = await Import.ImportAsync(
            Create(type, ["Name"], ["Ada"]),
            type,
            map,
            new TabularImportOptions
            {
                Binding = ColumnBinding.ByHeaderName,
                Translations = translation is null
                    ? null
                    : new Dictionary<string, string> { ["name"] = translation },
            },
            Token);

        // Assert
        Assert.Equal(TabularErrorKind.Configuration, report.Errors[0].Kind);
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task PaddedHeader_ReadsBackByName(
        TabularType type
    )
    {
        // Arrange
        var map = TabularMap
            .For<Person>()
            .Column(x => x.Name, index: 0, configure: c => c.Header(" Name "))
            .Build();

        // Act
        // WHY: The round trip of the own output is the checked contract, so export and reimport form one action.
        var (_, file) = await Export.ExportAsync(
            new[] { new Person("Ada") },
            map,
            type,
            new TabularExportOptions { WriteHeader = true },
            Token);

        var report = await Import.ImportAsync(
            file!,
            type,
            map,
            new TabularImportOptions { Binding = ColumnBinding.ByHeaderName },
            Token);

        // Assert
        Assert.Equal(
            "Ada",
            Assert.Single(report.Items!)
                .Name);
    }

    [Fact]
    public async Task NormalizeHeaderThrowingAnUnrequestedCancellation_IsACallbackError()
    {
        // Arrange
        var map = TabularMap
            .For<Person>()
            .Column(x => x.Name, index: 0, configure: c => c.Header("Name"))
            .Build();

        // Act
        var report = await CsvInstance.ImportAsync(
            TestInput.Utf8("Name\nAda"),
            map,
            new CsvImportOptions
            {
                Binding = ColumnBinding.ByHeaderName,
                NormalizeHeader = _ => throw new OperationCanceledException(),
            },
            Token);

        // Assert
        Assert.Equal(
            (ReportStatus.Failed, TabularErrorCodes.Callback.Failed),
            (report.Status, report.FatalError?.Code));
    }

    [Fact]
    public async Task HeaderComparison_TrimsAndIgnoresCase()
    {
        // Act
        var report = await CsvInstance.ImportAsync(
            TestInput.Utf8(" POSTAL CODE \nAnna Maria"),
            s_postalCode,
            new CsvImportOptions { Binding = ColumnBinding.ByHeaderName },
            Token);

        // Assert
        Assert.Equal(
            "Anna Maria",
            Assert.Single(report.Items!)
                .Name);
    }

    [Fact]
    public async Task HeaderComparison_UsesNormalizeHeader()
    {
        // Act
        var report =
            await CsvInstance.ImportAsync(TestInput.Utf8("PostalCode\nx"), s_postalCode, s_withoutWhitespace, Token);

        // Assert
        Assert.Equal(
            "x",
            Assert.Single(report.Items!)
                .Name);
    }

    [Fact]
    public async Task HeaderComparison_WithTwoHeadersEqualAfterNormalizing_IsAmbiguous()
    {
        // Act
        var report = await CsvInstance.ImportAsync(
            TestInput.Utf8("PostalCode,Postal Code\nx,y"),
            s_postalCode,
            s_withoutWhitespace,
            Token);

        // Assert
        Assert.Equal(
            TabularErrorCodes.Schema.AmbiguousColumn,
            Assert.Single(report.Errors)
                .Code);
    }

    [Fact]
    public async Task MissingRequiredHeader_IsOneMissingColumnAndImportsNothing()
    {
        // Act
        var report = await CsvInstance.ImportAsync(
            TestInput.Utf8("Eintrittsdatum\n2024-04-01\n2024-04-02"),
            s_optionalHire,
            new CsvImportOptions { Binding = ColumnBinding.ByHeaderName },
            Token);

        // Assert
        Assert.Equal(
            TabularErrorCodes.Schema.MissingColumn,
            Assert.Single(report.Errors)
                .Code);

        Assert.Equal(0, report.RowsImported);
    }

    [Fact]
    public async Task MissingOptionalHeader_IsAnEmptyValue()
    {
        // Act
        var report = await CsvInstance.ImportAsync(
            TestInput.Utf8("E-Mail\nada@example.org"),
            s_optionalHire,
            new CsvImportOptions { Binding = ColumnBinding.ByHeaderName },
            Token);

        // Assert
        Assert.Null(
            Assert.Single(report.Items!)
                .DateOfHire);
    }

    [Fact]
    public void OptionalHeader_OnNonNullableValueType_FailsAtBuild()
    {
        // Act
        var exception = Record.Exception(() => TabularMap
            .For<IdUser>()
            .Column(x => x.Id, index: 0, configure: c => c.OptionalHeader())
            .Build());

        // Assert
        Assert.IsType<TabularConfigurationException>(exception);
    }

    [GeneratedRegex(@"\s")]
    private static partial Regex Whitespace();
}
