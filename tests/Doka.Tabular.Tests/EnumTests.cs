using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Tests;

/// <summary>Enum texts, aliases, numeric input, and flags.</summary>
public sealed partial class EnumTests
{
    [SuppressMessage("Design",
        "CA1069:Enums values should not be duplicated",
        Justification = "The alias case needs two members with the same value.")]
    public enum AliasStatus
    {
        Active = 1,
        Enabled = 1,
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public enum Colliding
    {
        [JsonStringEnumMemberName("active")]
        Active = 1,

        [JsonStringEnumMemberName("Active")]
        Enabled = 2,
    }

    [Flags]
    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public enum Combined
    {
        ReadWrite = 3,
        WriteDelete = 6,
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public enum ContactStatus
    {
        Inactive = 0,
        Active = 1,
        Suspended = 2,
    }

    [Flags]
    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public enum NoZero
    {
        Read = 1,
        Write = 2,
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public enum Numbered
    {
        Draft = 1,

        [JsonStringEnumMemberName("1")]
        Published = 2,
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public enum OrderBy
    {
        [JsonStringEnumMemberName("asc")]
        Ascending = 1,

        Descending = 2,
    }

    [Flags]
    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public enum Ordered
    {
        Delete = 4,
        Read = 1,
        Write = 2,
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public enum PaddedNumbered
    {
        Draft = 1,

        [JsonStringEnumMemberName("01")]
        Published = 2,
    }

    [Flags]
    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public enum Permissions
    {
        None = 0,
        Read = 1,
        Write = 2,
        Delete = 4,
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public enum SelfNumbered
    {
        Draft = 1,

        [JsonStringEnumMemberName("2")]
        Published = 2,
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public enum SelfPadded
    {
        Draft = 1,

        [JsonStringEnumMemberName("02")]
        Published = 2,
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public enum SignedNumbered
    {
        Draft = 1,

        [JsonStringEnumMemberName("+1")]
        Published = 2,
    }

    public enum Small : short
    {
        Minus = -1,
        One = 1,
        Thousand = 1000,
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public enum SpacedNumbered
    {
        Draft = 1,

        [JsonStringEnumMemberName(" 01 ")]
        Published = 2,
    }

    private static readonly TabularMap<Value<OrderBy>> s_throwingKeys =
        Map<OrderBy>(c => c.TranslationKeys(_ => throw new InvalidOperationException("No key.")));

    private static readonly Dictionary<string, string> s_upTranslation = new() { ["order.Ascending"] = "Up" };

    private static readonly Value<AliasStatus>[] s_activeAlias = [new(1, AliasStatus.Active)];

    private static readonly TabularMap<Value<AliasStatus>> s_aliasKeys =
        Map<AliasStatus>(c => c.TranslationKeys(_ => "status.one"));

    private static TabularMap<Value<T>> Map<T>(
        Action<TabularColumnBuilder<T>>? configure = null
    ) => TabularMap
        .For<Value<T>>()
        .Column(x => x.Key, index: 0)
        .Column(x => x.Item, index: 1, configure: configure)
        .Build();

    private static async Task<ImportReport<Value<T>>> ReadAsync<T>(
        TabularType type,
        object? cell,
        TabularMap<Value<T>>? map = null,
        TabularImportOptions? options = null
    ) => await Import.ImportAsync(Create(type, [1, cell]),
        type,
        map ?? Map<T>(),
        options ?? new TabularImportOptions(),
        Token);

    /// <summary>The value of the single record of a successful read.</summary>
    private static T ValueOf<T>(
        ImportReport<Value<T>> report
    )
    {
        Assert.True(report.Status == ReportStatus.Succeeded,
            string.Join(" | ", report.Errors.Select(error => error.Code.Value)));

        return Assert.Single(report.Items!)
            .Item;
    }

    /// <summary>Exports one value and returns the report and the written text of the value.</summary>
    private static async Task<(ExportReport Report, string? Text)> ExportTextAsync<T>(
        TabularType type,
        T value,
        TabularMap<Value<T>>? map = null,
        TabularExportOptions? options = null
    )
    {
        var (report, file) = await Export.ExportAsync(new[] { new Value<T>(1, value) },
            map ?? Map<T>(),
            type,
            options ?? new TabularExportOptions(),
            Token);

        return (report, file is null
            ? null
            : Cells(type, file)[0][1]);
    }

    public static TheoryData<TabularType, string> OrderTexts()
    {
        var data = new TheoryData<TabularType, string>();
        foreach (var type in new[] { TabularType.Csv, TabularType.Excel })
        {
            foreach (var text in new[] { "Ascending", "asc", "ASC" })
            {
                data.Add(type, text);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(OrderTexts))]
    public async Task AttributeText_AndMemberName_AreAcceptedCaseInsensitively(
        TabularType type,
        string text
    )
    {
        // Act
        var report = await ReadAsync<OrderBy>(type, text);

        // Assert
        Assert.Equal(OrderBy.Ascending, ValueOf(report));
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task NumberText_WithoutNumericInput_IsRejected(
        TabularType type
    )
    {
        // Act
        var report = await ReadAsync<OrderBy>(type, "0");

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task Export_WritesTheAttributeText(
        TabularType type
    )
    {
        // Act
        var (_, text) = await ExportTextAsync(type, OrderBy.Ascending);

        // Assert
        Assert.Equal("asc", text);
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task ExportMemberName_WritesTheMemberName(
        TabularType type
    )
    {
        // Act
        var (_, text) = await ExportTextAsync(type, OrderBy.Ascending, Map<OrderBy>(c => c.ExportMemberName()));

        // Assert
        Assert.Equal("Ascending", text);
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task ConcurrentTranslatedExports_UseTheTranslationsOfTheirCall(
        TabularType type
    )
    {
        // Arrange
        var translated = Map<OrderBy>(c => c.TranslationKeys(value => $"order.{value}"));
        var german = new TabularExportOptions
        {
            Translations = new Dictionary<string, string> { ["order.Ascending"] = "Aufsteigend" },
        };

        var english = new TabularExportOptions
        {
            Translations = new Dictionary<string, string> { ["order.Ascending"] = "Up" },
        };

        // Act
        var both = await Task.WhenAll(ExportTextAsync(type, OrderBy.Ascending, translated, german),
            ExportTextAsync(type, OrderBy.Ascending, translated, english));

        // Assert
        Assert.Equal(["Aufsteigend", "Up"], both.Select(result => result.Text));
    }

    [Fact]
    public async Task ThrowingTranslationKeyFunction_IsAConfigurationErrorOfTheImport()
    {
        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8("1,Up"),
            s_throwingKeys,
            new CsvImportOptions { Translations = s_upTranslation },
            Token);

        // Assert
        Assert.Equal(TabularErrorKind.Configuration,
            Assert.Single(report.Errors)
                .Kind);
    }

    [Fact]
    public async Task ThrowingTranslationKeyFunction_IsAConfigurationErrorOfTheExport()
    {
        // Act
        var report = await CsvInstance.ExportAsync(new[] { new Value<OrderBy>(1, OrderBy.Ascending) },
            s_throwingKeys,
            new MemoryStream(),
            new CsvExportOptions { Translations = s_upTranslation },
            Token);

        // Assert
        Assert.Equal(TabularErrorKind.Configuration,
            Assert.Single(report.Errors)
                .Kind);
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task NumericInput_WithoutItsOption_IsRejected(
        TabularType type
    )
    {
        // Act
        var report = await ReadAsync<ContactStatus>(type,
            type == TabularType.Csv
                ? "1"
                : 1);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task NumericInput_WithItsOption_IsRead(
        TabularType type
    )
    {
        // Act
        var report = await ReadAsync(type,
            type == TabularType.Csv
                ? "1"
                : 1,
            Map<ContactStatus>(c => c.AllowNumericInput()));

        // Assert
        Assert.Equal(ContactStatus.Active, ValueOf(report));
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task NumericInput_OfAnUndefinedValue_IsRejected(
        TabularType type
    )
    {
        // Act
        var report = await ReadAsync(type, "99", Map<ContactStatus>(c => c.AllowNumericInput()));

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task ExportAsNumber_WritesTheNumber(
        TabularType type
    )
    {
        // Act
        var (_, text) = await ExportTextAsync(type, ContactStatus.Active, Map<ContactStatus>(c => c.ExportAsNumber()));

        // Assert
        Assert.Equal("1", text);
    }

    [Theory]
    [InlineData("1", Small.One)]
    [InlineData("01", Small.One)]
    [InlineData("+1", Small.One)]
    [InlineData("-1", Small.Minus)]
    [InlineData("1000", Small.Thousand)]
    public async Task NumericInput_UnderShort_AcceptsPlainIntegers(
        string text,
        Small expected
    )
    {
        // Act
        var report = await ReadAsync(TabularType.Csv, text, Map<Small>(c => c.AllowNumericInput()));

        // Assert
        Assert.Equal(expected, ValueOf(report));
    }

    public static TheoryData<string, string> NonPlainIntegers()
    {
        var data = new TheoryData<string, string>();
        foreach (var culture in new[] { "", "de-DE" })
        {
            foreach (var text in new[]
                {
                    "1.0",
                    "1,0",
                    "1e0",
                    "0x1",
                    "1,000",
                    "1.000",
                    "+",
                    "32768",
                })
            {
                data.Add(culture, text);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(NonPlainIntegers))]
    public async Task NumericInput_UnderShort_RejectsEverythingButPlainIntegers(
        string culture,
        string text
    )
    {
        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8($"1,\"{text}\""),
            Map<Small>(c => c.AllowNumericInput()),
            new CsvImportOptions { Culture = CultureInfo.GetCultureInfo(culture) },
            Token);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    [Fact]
    public void CollidingTexts_AreAConfigurationError()
    {
        // Act
        var exception = Record.Exception(() => Map<Colliding>());

        // Assert
        Assert.IsType<TabularConfigurationException>(exception);
    }

    [Fact]
    public async Task NumberShapedText_IsReadAsTheTextOfItsMember()
    {
        // Act
        var report = await ReadAsync<Numbered>(TabularType.Csv, "1");

        // Assert
        Assert.Equal(Numbered.Published, ValueOf(report));
    }

    [Fact]
    public async Task NumberShapedText_OfTheSameValue_IsReadWithNumericInput()
    {
        // Act
        var report = await ReadAsync(TabularType.Csv, "2", Map<SelfNumbered>(c => c.AllowNumericInput()));

        // Assert
        Assert.Equal(SelfNumbered.Published, ValueOf(report));
    }

    [Fact]
    public void NumberShapedText_EqualToTheNumberOfAnotherValue_FailsAtBuildWithNumericInput()
    {
        // Act
        var exception = Record.Exception(() => Map<Numbered>(c => c.AllowNumericInput()));

        // Assert
        Assert.IsType<TabularConfigurationException>(exception);
    }

    [Fact]
    public void NumberShapedTextAfterTheTrimOfTheField_OfAnotherValue_FailsAtBuild()
    {
        // Act
        var exception = Record.Exception(() => Map<SpacedNumbered>(c => c
            .AllowNumericInput()
            .Trim()));

        // Assert
        Assert.IsType<TabularConfigurationException>(exception);
    }

    [Fact]
    public async Task NumberShapedTextAfterTheTrimOfTheCall_OfAnotherValue_FailsBeforeTheFirstRow()
    {
        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8("1,1"),
            Map<SpacedNumbered>(c => c.AllowNumericInput()),
            new CsvImportOptions { TrimFields = true },
            Token);

        // Assert
        Assert.Equal((TabularErrorCodes.Configuration.Invalid, 0L),
            (Assert.Single(report.Errors)
                .Code, report.RowsRead));
    }

    [Fact]
    public void NumberShapedTextAfterTheTrimOfTheField_InAConversion_IsAConfigurationError()
    {
        // Act
        var exception = Record.Exception(() => Map<SpacedNumbered>(c => c
            .Trim()
            .Convert(value => value,
                value => value,
                configure: b => b.AllowNumericInput())));

        // Assert
        Assert.IsType<TabularConfigurationException>(exception);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NumberShapedText_OfAnotherValue_IsAConfigurationErrorWithNumericInput(
        bool withSign
    )
    {
        // Act
        var exception = Record.Exception(() => withSign
            ? Map<SignedNumbered>(c => c.AllowNumericInput())
            : Map<PaddedNumbered>(c => c.AllowNumericInput()));

        // Assert
        Assert.IsType<TabularConfigurationException>(exception);
    }

    [Fact]
    public async Task PaddedNumberText_IsReadAsTheTextOfItsMember()
    {
        // Act
        var report = await ReadAsync<PaddedNumbered>(TabularType.Csv, "01");

        // Assert
        Assert.Equal(PaddedNumbered.Published, ValueOf(report));
    }

    [Fact]
    public async Task SignedNumber_OfAValueWithAPaddedTextOfItself_IsReadWithNumericInput()
    {
        // Act
        var report = await ReadAsync(TabularType.Csv, "+02", Map<SelfPadded>(c => c.AllowNumericInput()));

        // Assert
        Assert.Equal(SelfPadded.Published, ValueOf(report));
    }

    [Fact]
    public async Task NumberShapedTranslation_OfAnotherValue_FailsBeforeTheFirstRowWithNumericInput()
    {
        // Arrange
        var translated = Map<OrderBy>(c => c
            .AllowNumericInput()
            .TranslationKeys(value => $"order.{value}"));

        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8("1,2"),
            translated,
            new CsvImportOptions
            {
                Translations = new Dictionary<string, string> { ["order.Descending"] = "001" },
            },
            Token);

        // Assert
        Assert.Equal((TabularErrorCodes.Configuration.Invalid, 0L),
            (Assert.Single(report.Errors)
                .Code, report.RowsRead));
    }

    [Fact]
    public void Aliases_WithoutExportChoice_FailAtBuild()
    {
        // Act
        var exception = Record.Exception(() => Map<AliasStatus>());

        // Assert
        Assert.IsType<TabularConfigurationException>(exception);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Aliases_WithAnExportChoice_Build(
        bool asNumber
    )
    {
        // Act
        var exception = Record.Exception(() => asNumber
            ? Map<AliasStatus>(c => c.ExportAsNumber())
            : Map<AliasStatus>(c => c.ExportAlias(nameof(AliasStatus.Active))));

        // Assert
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AliasesWithTranslationKeys_WithoutTheirTranslation_FailBeforeWriting(
        bool fixedFallback
    )
    {
        // Arrange
        var options = fixedFallback
            ? new CsvExportOptions
            {
                Translations = new Dictionary<string, string>(),
                MissingTranslation = MissingTranslationBehavior.UseFixedText,
            }
            : new CsvExportOptions();

        // Act
        var report = await CsvInstance.ExportAsync(s_activeAlias, s_aliasKeys, new MemoryStream(), options, Token);

        // Assert
        Assert.Equal((ReportStatus.Failed, TabularErrorKind.Configuration, 0L),
            (report.Status, report.Errors[0].Kind, report.RowsRead));
    }

    [Fact]
    public async Task AliasesWithTranslationKeys_WriteTheirTranslation()
    {
        // Act
        var (report, file) = await CsvInstance.ExportAsync(s_activeAlias,
            s_aliasKeys,
            new CsvExportOptions { Translations = new Dictionary<string, string> { ["status.one"] = "On" } },
            Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        Assert.Equal("1,On", TestInput.Utf8Text(file!));
    }

    [Fact]
    public void ExportAlias_OfAnUnknownMember_FailsAtBuild()
    {
        // Act
        var exception = Record.Exception(() => Map<AliasStatus>(c => c.ExportAlias("Unknown")));

        // Assert
        Assert.IsType<TabularConfigurationException>(exception);
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task Aliases_WriteTheChosenMemberOfTheSameValue(
        TabularType type
    )
    {
        // Act
        var (_, text) = await ExportTextAsync(type,
            AliasStatus.Active,
            Map<AliasStatus>(c => c.ExportAlias(nameof(AliasStatus.Enabled))));

        // Assert
        Assert.Equal("Enabled", text);
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task Aliases_WriteTheChosenText(
        TabularType type
    )
    {
        // Act
        var (_, text) = await ExportTextAsync(type,
            AliasStatus.Active,
            Map<AliasStatus>(c => c.ExportAlias(nameof(AliasStatus.Active))));

        // Assert
        Assert.Equal("Active", text);
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task Aliases_WithExportAsNumber_WriteTheNumber(
        TabularType type
    )
    {
        // Act
        var (_, text) = await ExportTextAsync(type, AliasStatus.Enabled, Map<AliasStatus>(c => c.ExportAsNumber()));

        // Assert
        Assert.Equal("1", text);
    }

    [Fact]
    public async Task EmptyEnumField_IsNullForNullable()
    {
        // Act
        var report = await ReadAsync<ContactStatus?>(TabularType.Csv, "");

        // Assert
        Assert.Null(ValueOf(report));
    }

    [Fact]
    public async Task EmptyEnumField_IsAnErrorForAPlainEnum()
    {
        // Act
        var report = await ReadAsync<ContactStatus>(TabularType.Csv, "");

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    [Fact]
    public async Task ZeroWithoutMember_IsRejectedForAPlainEnumWithNumericInput()
    {
        // Act
        var report = await ReadAsync(TabularType.Csv, "0", Map<OrderBy>(c => c.AllowNumericInput()));

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    [Fact]
    public async Task NumberText_OfAFlagsEnumWithoutNumericInput_IsRejected()
    {
        // Act
        var report = await ReadAsync<NoZero>(TabularType.Csv, "1");

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    public sealed record Value<T>(
        int Key,
        T Item
    );
}
