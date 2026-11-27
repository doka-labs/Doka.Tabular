using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Tests;

public sealed partial class EnumTests
{
    private static readonly Dictionary<string, string> s_permissionTexts = new()
    {
        ["p.None"] = "Keine",
        ["p.Read"] = "Lesen, erweitert",
        ["p.Write"] = "Schreiben",
        ["p.Delete"] = "Löschen",
    };

    [Theory]
    [InlineData("Read, Write", Permissions.Read | Permissions.Write)]
    [InlineData("Read,,Write", Permissions.Read | Permissions.Write)]
    [InlineData("Read,read,Write", Permissions.Read | Permissions.Write)]
    [InlineData(",Read", Permissions.Read)]
    [InlineData("Read,", Permissions.Read)]
    public async Task Flags_ReadSeparatedMembers(
        string text,
        Permissions expected
    )
    {
        // Act
        var report = await ReadAsync<Permissions>(TabularType.Csv, text);

        // Assert
        Assert.Equal(expected, ValueOf(report));
    }

    [Theory]
    [InlineData("Execute")]
    [InlineData("8")]
    [InlineData("Read,2")]
    public async Task Flags_RejectUnknownMembersAndBits(
        string text
    )
    {
        // Act
        var report = await ReadAsync(TabularType.Csv, text, Map<Permissions>(c => c.AllowNumericInput()));

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task Flags_ExportTheMembersWithTheDefaultSeparator(
        TabularType type
    )
    {
        // Act
        var (_, text) = await ExportTextAsync(type, Permissions.Read | Permissions.Write);

        // Assert
        Assert.Equal("Read,Write", text);
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task Flags_ExportTheMembersWithTheChosenSeparator(
        TabularType type
    )
    {
        // Act
        var (_, text) = await ExportTextAsync(
            type,
            Permissions.Read | Permissions.Write,
            Map<Permissions>(c => c.FlagsSeparator('|')));

        // Assert
        Assert.Equal("Read|Write", text);
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task Flags_ExportTheMembersInAscendingBitOrder(
        TabularType type
    )
    {
        // Act
        var (_, text) = await ExportTextAsync(type, (Ordered)7);

        // Assert
        Assert.Equal("Read,Write,Delete", text);
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task Flags_ReadBack(
        TabularType type
    )
    {
        // Act
        // WHY: The round trip of the own output is the checked contract, so export and reimport form one action.
        var (_, file) = await Export.ExportAsync(
            new[] { new Value<Permissions>(1, Permissions.Read | Permissions.Write) },
            Map<Permissions>(),
            type,
            Token);

        var report = await Import.ImportAsync(file!, type, Map<Permissions>(), Token);

        // Assert
        Assert.Equal(Permissions.Read | Permissions.Write, ValueOf(report));
    }

    [Fact]
    public async Task Flags_AreAnExcelTextCell()
    {
        // Act
        var (_, file) = await Export.ExportAsync(
            new[] { new Value<Permissions>(1, Permissions.Read | Permissions.Write) },
            Map<Permissions>(),
            TabularType.Excel,
            Token);

        // Assert
        Assert.Equal("inlineStr", CellTypes(file!, 1)[1]);
    }

    [Theory]
    [InlineData('1')]
    [InlineData((char)0x0661)]
    [InlineData((char)0x0967)]
    [InlineData((char)0xFF15)]
    [InlineData(' ')]
    [InlineData((char)0x00A0)]
    [InlineData('"')]
    [InlineData('+')]
    [InlineData('-')]
    public void FlagsSeparatorThatIsADigitOrAnotherReservedCharacter_IsAConfigurationError(
        char separator
    )
    {
        // Act
        var exception = Record.Exception(() => Map<Permissions>(c => c.FlagsSeparator(separator)));

        // Assert
        Assert.IsType<TabularConfigurationException>(exception);
    }

    [Theory]
    [InlineData(';')]
    [InlineData('|')]
    [InlineData('/')]
    [InlineData((char)0x00B2)]
    public void FlagsSeparatorThatIsNoDigit_Builds(
        char separator
    )
    {
        // Act
        var exception = Record.Exception(() => Map<Permissions>(c => c.FlagsSeparator(separator)));

        // Assert
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("3")]
    [InlineData("6")]
    [InlineData("7")]
    public async Task Flags_CombinedMembers_AcceptWholeValues(
        string text
    )
    {
        // Act
        var report = await ReadAsync(TabularType.Csv, text, Map<Combined>(c => c.AllowNumericInput()));

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("4")]
    [InlineData("5")]
    public async Task Flags_CombinedMembers_RejectPartsOfAMember(
        string text
    )
    {
        // Act
        var report = await ReadAsync(TabularType.Csv, text, Map<Combined>(c => c.AllowNumericInput()));

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    [Fact]
    public async Task Flags_CombinedMembers_ExportTheWholeMembers()
    {
        // Act
        var (_, text) = await ExportTextAsync(TabularType.Csv, (Combined)7);

        // Assert
        Assert.Equal("ReadWrite,WriteDelete", text);
    }

    [Fact]
    public async Task Flags_CombinedMembers_PartOfAMember_FailsTheExportAtItsRow()
    {
        // Act
        var report = await CsvInstance.ExportAsync(
            new[] { new Value<Combined>(4, (Combined)1) },
            Map<Combined>(),
            new MemoryStream(),
            Token);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
        Assert.Equal(1, report.FatalError?.Location.RowNumber);
    }

    [Fact]
    public async Task FlagsZeroWithoutMember_IsReadWithNumericInput()
    {
        // Act
        var report = await ReadAsync(TabularType.Csv, "0", Map<NoZero>(c => c.AllowNumericInput()));

        // Assert
        Assert.Equal((NoZero)0, ValueOf(report));
    }

    [Fact]
    public async Task FlagsZeroWithoutMember_IsRejectedWithoutNumericInput()
    {
        // Act
        var report = await ReadAsync<NoZero>(TabularType.Csv, "0");

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    [Fact]
    public async Task FlagsWithOnlySeparators_AreNullForNullable()
    {
        // Act
        var report = await ReadAsync<NoZero?>(TabularType.Csv, ",");

        // Assert
        Assert.Null(ValueOf(report));
    }

    [Fact]
    public async Task FlagsZeroWithoutMember_FailsTheExportWithoutAText()
    {
        // Act
        var report = await CsvInstance.ExportAsync(
            new[] { new Value<NoZero>(1, 0) },
            Map<NoZero>(),
            new MemoryStream(),
            Token);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    [Fact]
    public async Task FlagsZeroWithoutMember_IsWrittenWithItsTranslation()
    {
        // Arrange
        var options = new TabularExportOptions
        {
            Translations = new Dictionary<string, string>
            {
                ["p.0"] = "Keine",
                ["p.Read"] = "Lesen",
                ["p.Write"] = "Schreiben",
            },
        };

        // Act
        var (_, text) = await ExportTextAsync(
            TabularType.Csv,
            (NoZero)0,
            Map<NoZero>(c => c.TranslationKeys(value => $"p.{value}")),
            options);

        // Assert
        Assert.Equal("Keine", text);
    }

    [Fact]
    public async Task FlagsZeroWithoutMember_IsWrittenAsNumber()
    {
        // Act
        var (_, text) = await ExportTextAsync(TabularType.Csv, (NoZero)0, Map<NoZero>(c => c.ExportAsNumber()));

        // Assert
        Assert.Equal("0", text);
    }

    [Fact]
    public async Task Flags_EmptyTranslationOfZeroWithoutMember_IsAConfigurationError()
    {
        // Act
        var report = await CsvInstance.ExportAsync(
            new[] { new Value<NoZero>(1, NoZero.Read) },
            Map<NoZero>(c => c.TranslationKeys(value => $"p.{value}")),
            new MemoryStream(),
            new CsvExportOptions
            {
                Translations = new Dictionary<string, string>
                {
                    ["p.0"] = "",
                    ["p.Read"] = "Lesen",
                    ["p.Write"] = "Schreiben",
                },
            },
            Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Configuration.Invalid, (report.FatalError ?? report.Errors[0]).Code);
    }

    [Fact]
    public async Task Flags_TranslationContainingTheSeparator_IsAConfigurationError()
    {
        // Act
        var report = await CsvInstance.ExportAsync(
            new[] { new Value<Permissions>(1, Permissions.Read) },
            Map<Permissions>(c => c.TranslationKeys(value => $"p.{value}")),
            new MemoryStream(),
            new CsvExportOptions { Translations = s_permissionTexts },
            Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Configuration.Invalid, (report.FatalError ?? Assert.Single(report.Errors)).Code);
    }

    [Fact]
    public async Task Flags_TranslationWithoutTheChosenSeparator_IsWritten()
    {
        // Act
        var report = await CsvInstance.ExportAsync(
            new[] { new Value<Permissions>(1, Permissions.Read) },
            Map<Permissions>(c => c
                .TranslationKeys(value => $"p.{value}")
                .FlagsSeparator('|')),
            new MemoryStream(),
            new CsvExportOptions { Translations = s_permissionTexts },
            Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
    }

    [Fact]
    public async Task Flags_TranslatedZeroWithoutZeroMember_IsReadBack()
    {
        // Arrange
        var map = Map<NoZero>(c => c.TranslationKeys(value => $"p.{value}"));
        var texts = new Dictionary<string, string>
        {
            ["p.0"] = "Keine",
            ["p.Read"] = "Lesen",
            ["p.Write"] = "Schreiben",
        };

        // Act
        var report = await ReadAsync(TabularType.Csv, "Keine", map, new TabularImportOptions { Translations = texts });

        // Assert
        Assert.Equal((NoZero)0, ValueOf(report));
    }
}
