using System.Text.Json.Serialization;
using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Tests;

/// <summary>Trimming applies the same way to the base type of a custom conversion; blank labels cannot read back.</summary>
public sealed partial class ConversionTests
{
    public enum Blank
    {
        [JsonStringEnumMemberName("   ")]
        Ready = 1,
    }

    private static readonly Dictionary<string, string> s_emptyTranslations = new()
    {
        ["yes"] = "",
        ["no"] = "no",
        ["null"] = "",
    };

    [Fact]
    public async Task TrimOfTheColumn_AppliesToTheLabelsOfTheBaseTypeOnReadBack()
    {
        // Act
        var report = await ReadBackAsync("y", TrimmedYesFlag());

        // Assert
        Assert.Equal("y", ValueOf(report));
    }

    [Fact]
    public async Task TrimOfTheColumn_AppliesToTheLabelsOfTheBaseTypeOnImport()
    {
        // Act
        var report = await ReadAsync(TabularType.Csv, " yes ", TrimmedYesFlag());

        // Assert
        Assert.Equal("y", ValueOf(report));
    }

    private static TabularMap<Value<string>> TrimmedYesFlag() => Map<string>(c => c
        .Trim()
        .Convert(
            value => value
                ? "y"
                : "n",
            text => text == "y",
            configure: b => b
                .Trim(false)
                .TrueText(" yes ")));

    [Theory]
    [InlineData("bool text")]
    [InlineData("enum label")]
    [InlineData("null marker")]
    [InlineData("conversion null marker")]
    public void BlankLabelOrNullMarker_WithTrimOfTheColumn_IsAConfigurationError(
        string kind
    )
    {
        // Act
        var exception = Record.Exception(() => kind switch
        {
            "bool text" => Map<bool>(c => c
                .TrueText(" ")
                .Trim()),
            "enum label" => Map<Blank>(c => c.Trim()),
            "null marker" => Map<string?>(c => c
                .NullText(" ")
                .Trim()),
            _ => Map<string>(c => c
                .Trim()
                .Convert<string?, string>(text => text ?? "", text => text, configure: b => b.NullText(" "))),
        });

        // Assert
        Assert.IsType<TabularConfigurationException>(exception);
    }

    [Theory]
    [InlineData("bool text")]
    [InlineData("enum label")]
    [InlineData("null marker")]
    [InlineData("bool translation")]
    [InlineData("empty translation")]
    public async Task BlankLabelOrNullMarker_WithTrimOfTheCall_IsAConfigurationReport(
        string kind
    )
    {
        // Arrange
        var options = new CsvImportOptions
        {
            TrimFields = true,
            Translations = new Dictionary<string, string>
            {
                ["yes"] = " ",
                ["no"] = "no",
                ["null"] = " ",
            },
        };

        // Act
        var errors = kind switch
        {
            "bool text" => (await CsvInstance.ImportAsync(
                    TestInput.Utf8("x"),
                    Map<bool>(c => c.TrueText(" ")),
                    options,
                    Token))
                .Errors,
            "enum label" => (await CsvInstance.ImportAsync(TestInput.Utf8("x"), Map<Blank>(), options, Token)).Errors,
            "null marker" => (await CsvInstance.ImportAsync(
                TestInput.Utf8("x"),
                Map<string?>(c => c.NullText(" ")),
                options,
                Token)).Errors,
            "bool translation" => (await CsvInstance.ImportAsync(
                TestInput.Utf8("x"),
                Map<bool>(c => c.TranslationKeys("yes", "no")),
                options,
                Token)).Errors,
            _ => (await CsvInstance.ImportAsync(
                TestInput.Utf8("x"),
                Map<string?>(c => c.EmptyTranslationKey("null")),
                options,
                Token)).Errors,
        };

        // Assert
        Assert.Equal(TabularErrorKind.Configuration, errors[0].Kind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyTranslationOfABoolTextOrNullMarker_WithoutTrim_IsAConfigurationErrorOfTheImport(
        bool marker
    )
    {
        // Act
        var report = marker
            ? (await CsvInstance.ImportAsync(
                TestInput.Utf8("x"),
                Map<string?>(c => c.EmptyTranslationKey("null")),
                new CsvImportOptions { Translations = s_emptyTranslations },
                Token)).Errors
            : (await CsvInstance.ImportAsync(
                TestInput.Utf8("no"),
                Map<bool>(c => c.TranslationKeys("yes", "no")),
                new CsvImportOptions { Translations = s_emptyTranslations },
                Token)).Errors;

        // Assert
        Assert.Equal(
            TabularErrorKind.Configuration,
            Assert.Single(report)
                .Kind);
    }

    [Fact]
    public async Task EmptyTranslationOfABoolText_WithoutTrim_FailsTheExportBeforeWriting()
    {
        // Arrange
        var target = new MemoryStream();

        // Act
        var report = await CsvInstance.ExportAsync(
            new[] { new Value<bool>(false), new Value<bool>(true) },
            Map<bool>(c => c.TranslationKeys("yes", "no")),
            target,
            new CsvExportOptions { Translations = s_emptyTranslations },
            Token);

        // Assert
        Assert.Equal(
            TabularErrorKind.Configuration,
            Assert.Single(report.Errors)
                .Kind);

        Assert.Equal(0L, target.Length);
    }

    [Fact]
    public async Task EmptyTranslationOfANullMarker_WithoutTrim_FailsTheExportBeforeWriting()
    {
        // Arrange
        var target = new MemoryStream();

        // Act
        var report = await CsvInstance.ExportAsync(
            new[] { new Value<string?>("x"), new Value<string?>(null) },
            Map<string?>(c => c.EmptyTranslationKey("null")),
            target,
            new CsvExportOptions { Translations = s_emptyTranslations },
            Token);

        // Assert
        Assert.Equal(
            TabularErrorKind.Configuration,
            Assert.Single(report.Errors)
                .Kind);

        Assert.Equal(0L, target.Length);
    }

    [Fact]
    public async Task BlankBoolText_WithoutTrim_ReadsBack()
    {
        // Act
        var report = await ReadBackAsync(true, Map<bool>(c => c.TrueText(" ")));

        // Assert
        Assert.True(ValueOf(report));
    }

    [Fact]
    public async Task BlankEnumLabel_WithoutTrim_ReadsBack()
    {
        // Act
        var report = await ReadBackAsync(Blank.Ready, Map<Blank>());

        // Assert
        Assert.Equal(Blank.Ready, ValueOf(report));
    }

    [Fact]
    public async Task BlankNullMarker_WithoutTrim_ReadsBack()
    {
        // Act
        var report = await ReadBackAsync(null, Map<string?>(c => c.NullText(" ")));

        // Assert
        Assert.Null(ValueOf(report));
    }
}
