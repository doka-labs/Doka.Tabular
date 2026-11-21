using System.Numerics;
using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Tests;

/// <summary>Numbers, text, Guid, empty values, custom conversions, and bool.</summary>
public sealed partial class ConversionTests
{
    private static readonly Flag[] s_flagRows = [new(1, true), new(2, false), new(3, null)];

    private static readonly TabularMap<Flag> s_flags = TabularMap
        .For<Flag>()
        .Column(x => x.Key, index: 0)
        .Column(x => x.Value, index: 1)
        .Build();

    private static readonly TabularMap<Flag> s_ownFlagTexts = TabularMap
        .For<Flag>()
        .Column(x => x.Key, index: 0)
        .Column(x => x.Value,
            index: 1,
            configure: c => c
                .TrueText("Ja")
                .FalseText("Nein"))
        .Build();

    private static readonly TabularMap<Flag> s_translatedFlags = TabularMap
        .For<Flag>()
        .Column(x => x.Key, index: 0)
        .Column(x => x.Value, index: 1, configure: c => c.TranslationKeys(trueKey: "t", falseKey: "f"))
        .Build();

    private static readonly TabularMap<Customer> s_customers = TabularMap
        .For<Customer>()
        .Column(x => x.Number,
            index: 0,
            configure: c => c.Convert<string, CustomerNumber>(
                read: text => text.StartsWith("K-", StringComparison.Ordinal)
                    ? new CustomerNumber(text)
                    : throw new InvalidCustomerNumberException(),
                write: number => number.Text))
        .Expect<InvalidCustomerNumberException>(TabularErrorCode.Create("customer.invalid"))
        .Build();

    private static TabularMap<Value<T>> Map<T>(
        Action<TabularColumnBuilder<T>>? configure = null
    ) => TabularMap
        .For<Value<T>>()
        .Column(x => x.Item, index: 0, configure: configure)
        .Build();

    private static async Task<ImportReport<Value<T>>> ReadAsync<T>(
        TabularType type,
        object? cell,
        TabularMap<Value<T>>? map = null,
        CultureInfo? culture = null
    ) => await Import.ImportAsync(Create(type, [cell]),
        type,
        map ?? Map<T>(),
        new TabularImportOptions { Culture = culture },
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

    /// <summary>The code of the single error of a read.</summary>
    private static TabularErrorCode ErrorOf<T>(
        ImportReport<Value<T>> report
    ) => (report.FatalError ?? Assert.Single(report.Errors)).Code;

    /// <summary>Every error code of a read, without asserting their number.</summary>
    private static TabularErrorCode[] Codes<T>(
        ImportReport<Value<T>> report
    ) =>
    [
        .. report.Errors.Select(error => error.Code), .. report.FatalError is { } fatal
            ? [fatal.Code]
            : Array.Empty<TabularErrorCode>(),
    ];

    [Theory]
    [InlineData("-128")]
    [InlineData("127")]
    public async Task SByte_AcceptsItsRange(
        string text
    )
    {
        // Act
        var report = await ReadAsync<sbyte>(TabularType.Csv, text);

        // Assert
        Assert.Equal(sbyte.Parse(text, CultureInfo.InvariantCulture), ValueOf(report));
    }

    [Theory]
    [InlineData("-9223372036854775808")]
    [InlineData("9223372036854775807")]
    public async Task Long_AcceptsItsRange(
        string text
    )
    {
        // Act
        var report = await ReadAsync<long>(TabularType.Csv, text);

        // Assert
        Assert.Equal(long.Parse(text, CultureInfo.InvariantCulture), ValueOf(report));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("18446744073709551615")]
    public async Task ULong_AcceptsItsRange(
        string text
    )
    {
        // Act
        var report = await ReadAsync<ulong>(TabularType.Csv, text);

        // Assert
        Assert.Equal(ulong.Parse(text, CultureInfo.InvariantCulture), ValueOf(report));
    }

    [Fact]
    public async Task Int128_AcceptsItsMaximum()
    {
        // Act
        var report = await ReadAsync<Int128>(TabularType.Csv, Int128.MaxValue.ToString(CultureInfo.InvariantCulture));

        // Assert
        Assert.Equal(Int128.MaxValue, ValueOf(report));
    }

    [Fact]
    public async Task BigInteger_AcceptsAValueBeyondEveryFixedSizeInteger()
    {
        // Act
        var report = await ReadAsync<BigInteger>(TabularType.Csv, "123456789012345678901234567890");

        // Assert
        Assert.Equal(BigInteger.Parse("123456789012345678901234567890", CultureInfo.InvariantCulture), ValueOf(report));
    }

    [Theory]
    [InlineData("sbyte", "128")]
    [InlineData("long", "9223372036854775808")]
    [InlineData("ulong", "-1")]
    public async Task Integer_OneBeyondItsRange_IsOutOfRange(
        string type,
        string text
    )
    {
        // Act
        var codes = type switch
        {
            "sbyte" => Codes(await ReadAsync<sbyte>(TabularType.Csv, text)),
            "long" => Codes(await ReadAsync<long>(TabularType.Csv, text)),
            _ => Codes(await ReadAsync<ulong>(TabularType.Csv, text)),
        };

        // Assert
        Assert.Equal([TabularErrorCodes.Conversion.OutOfRange], codes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeIntegers_WithoutConverter_FailAtBuild(
        bool withoutSign
    )
    {
        // Act
        var exception = Record.Exception(() => withoutSign
            ? Map<nuint>()
            : Map<nint>());

        // Assert
        Assert.IsType<TabularConfigurationException>(exception);
    }

    [Theory]
    [InlineData("de-DE", "1.200,99", "1200.99")]
    [InlineData("de-DE", "1200,99", "1200.99")]
    [InlineData("", "1,234.50", "1234.50")]
    [InlineData("", "1234.50", "1234.50")]
    [InlineData("de-DE", "1,234", "1.234")]
    [InlineData("", "1,234", "1234")]
    public async Task Decimal_FollowsTheCulture(
        string culture,
        string text,
        string expected
    )
    {
        // Act
        var report = await ReadAsync<decimal>(TabularType.Csv, text, culture: CultureInfo.GetCultureInfo(culture));

        // Assert
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), ValueOf(report));
    }

    [Fact]
    public async Task Decimal_InvalidGrouping_IsAFormatError()
    {
        // Act
        var report =
            await ReadAsync<decimal>(TabularType.Csv, "1.20.0,99", culture: CultureInfo.GetCultureInfo("de-DE"));

        // Assert
        Assert.Equal(TabularErrorCodes.Conversion.InvalidFormat, ErrorOf(report));
    }

    [Fact]
    public async Task IntegerWithDecimalPoint_IsAFormatErrorByDefault()
    {
        // Act
        var report = await ReadAsync<int>(TabularType.Csv, "42.0");

        // Assert
        Assert.Equal(TabularErrorCodes.Conversion.InvalidFormat, ErrorOf(report));
    }

    [Fact]
    public async Task IntegerWithDecimalPoint_AndAZeroFraction_IsReadWhenAllowed()
    {
        // Act
        var report = await ReadAsync(TabularType.Csv, "42.0", Map<int>(c => c.AllowDecimalPoint()));

        // Assert
        Assert.Equal(42, ValueOf(report));
    }

    [Fact]
    public async Task IntegerWithDecimalPoint_AndAFraction_IsRejectedAlsoWhenAllowed()
    {
        // Act
        var report = await ReadAsync(TabularType.Csv, "42.5", Map<int>(c => c.AllowDecimalPoint()));

        // Assert
        Assert.NotEmpty(Codes(report));
    }

    [Fact]
    public async Task Double_AcceptsAnExponent()
    {
        // Act
        var report = await ReadAsync<double>(TabularType.Csv, "1e3");

        // Assert
        Assert.Equal(1000d, ValueOf(report));
    }

    [Fact]
    public async Task DecimalWithExponent_IsAFormatErrorByDefault()
    {
        // Act
        var report = await ReadAsync<decimal>(TabularType.Csv, "1e3");

        // Assert
        Assert.Equal(TabularErrorCodes.Conversion.InvalidFormat, ErrorOf(report));
    }

    [Fact]
    public async Task DecimalWithExponent_IsReadWhenAllowed()
    {
        // Act
        var report = await ReadAsync(TabularType.Csv, "1e3", Map<decimal>(c => c.AllowExponent()));

        // Assert
        Assert.Equal(1000m, ValueOf(report));
    }

    [Theory]
    [InlineData("1e400")]
    [InlineData("1e-400")]
    public async Task Double_BeyondItsRange_IsOutOfRange(
        string text
    )
    {
        // Act
        var report = await ReadAsync<double>(TabularType.Csv, text);

        // Assert
        Assert.Equal(TabularErrorCodes.Conversion.OutOfRange, ErrorOf(report));
    }

    [Fact]
    public async Task NativeExcelNumber_WithDecimalDisplayIsAnInteger()
    {
        // Act
        var report = await ReadAsync<int>(TabularType.Excel, 42d);

        // Assert
        Assert.Equal(42, ValueOf(report));
    }

    [Fact]
    public async Task Decimal_WithTrailingZerosBeyondItsPrecision_IsRead()
    {
        // Act
        var report = await ReadAsync<decimal>(TabularType.Csv, "1.5000000000000000000000000000");

        // Assert
        Assert.Equal(1.50m, ValueOf(report));
    }

    [Fact]
    public async Task Decimal_WithADigitBeyondItsPrecision_IsAPrecisionLoss()
    {
        // Act
        var report = await ReadAsync<decimal>(TabularType.Csv, "1.00000000000000000000000000001");

        // Assert
        Assert.Equal(TabularErrorCodes.Conversion.PrecisionLoss, ErrorOf(report));
    }

    [Fact]
    public async Task DecimalRounding_RoundsToEven()
    {
        // Arrange
        var rounded = Map<decimal>(c => c.Rounding(2));

        // Act
        var (_, file) = await CsvInstance.ExportAsync(new[] { new Value<decimal>(1.245m), new Value<decimal>(1.255m) },
            rounded,
            Token);

        // Assert
        Assert.Equal([["1.24"], ["1.26"]], Cells(TabularType.Csv, file!));
    }

    [Fact]
    public async Task Double_IsWrittenAsTheShortestTextThatReadsBack()
    {
        // Act
        var (_, file) = await CsvInstance.ExportAsync(new[] { new Value<double>(0.1), new Value<double>(-0d) },
            Map<double>(),
            Token);

        // Assert
        Assert.Equal([["0.1"], ["0"]], Cells(TabularType.Csv, file!));
    }

    [Fact]
    public async Task DoubleNaN_FailsTheExportByDefault()
    {
        // Act
        var report = await CsvInstance.ExportAsync(new[] { new Value<double>(double.NaN) },
            Map<double>(),
            new MemoryStream(),
            Token);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    [Fact]
    public async Task DoubleNaN_IsReadWhenNonFiniteValuesAreAllowed()
    {
        // Act
        var report = await ReadAsync(TabularType.Csv, "NaN", Map<double>(c => c.NonFiniteValues()));

        // Assert
        Assert.True(double.IsNaN(ValueOf(report)));
    }

    [Fact]
    public async Task ExcelDecimal_IsWrittenAsANumberCell()
    {
        // Act
        var (_, file) = await ExcelInstance.ExportAsync(new[] { new Value<decimal>(0.1m) }, Map<decimal>(), Token);

        // Assert
        Assert.Equal(["n"], CellTypes(file!, 1));
    }

    [Fact]
    public async Task ExcelDecimal_ReadsBackExactly()
    {
        // Arrange
        var map = Map<decimal>();

        // Act
        // WHY: The round trip of the own output is the checked contract, so export and reimport form one action.
        var (_, file) =
            await ExcelInstance.ExportAsync(new[] { new Value<decimal>(0.1m), new Value<decimal>(1000000000000000m) },
                map,
                Token);

        var report = await ExcelInstance.ImportAsync(file!, map, Token);

        // Assert
        Assert.Equal([0.1m, 1000000000000000m], report.Items!.Select(item => item.Item));
    }

    [Fact]
    public async Task ExcelDecimal_WithMoreThan15Digits_IsAPrecisionLossOfItsColumn()
    {
        // Act
        var report = await ExcelInstance.ExportAsync(new[] { new Value<decimal>(1234567890123456m) },
            Map<decimal>(),
            new MemoryStream(),
            Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Conversion.PrecisionLoss, report.FatalError?.Code);
        Assert.Equal("Item", report.FatalError!.Location.ColumnKey?.Value);
    }

    [Fact]
    public async Task ExcelDecimal_AsText_KeepsEveryDigit()
    {
        // Act
        var report = await ExcelInstance.ExportAsync(new[] { new Value<decimal>(1234567890123456m) },
            Map<decimal>(c => c.ExcelAsText()),
            Token);

        // Assert
        Assert.Equal("1234567890123456", Cells(TabularType.Excel, report.File!)[0][0]);
    }

    [Fact]
    public async Task ExcelULongMaximum_AsANumberCell_FailsTheExport()
    {
        // Act
        var report = await ExcelInstance.ExportAsync(new[] { new Value<ulong>(ulong.MaxValue) },
            Map<ulong>(),
            new MemoryStream(),
            Token);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    [Theory]
    [InlineData(1067, false, null, "1067")]
    [InlineData(1067, true, null, "01067")]
    [InlineData(92991000, false, null, "92991000")]
    [InlineData(12.5, false, "de-DE", "12.5")]
    [InlineData("01234567", false, null, "01234567")]
    public async Task ExcelCell_IntoString_UsesTheStoredValueAndOptionalPadding(
        object cell,
        bool padded,
        string? culture,
        string expected
    )
    {
        // Arrange
        var map = padded
            ? Map<string>(c => c.PadLeft(totalWidth: 5, paddingChar: '0'))
            : Map<string>();

        // Act
        var report = await ReadAsync(TabularType.Excel,
            cell,
            map,
            culture is null
                ? null
                : CultureInfo.GetCultureInfo(culture));

        // Assert
        Assert.Equal(expected, ValueOf(report));
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task EmptyCell_IntoString_IsEmpty(
        TabularType type
    )
    {
        // Act
        var report = await ReadAsync<string>(type, "");

        // Assert
        Assert.Equal("", ValueOf(report));
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task EmptyCell_IntoNullableInt_IsNull(
        TabularType type
    )
    {
        // Act
        var report = await ReadAsync<int?>(type, "");

        // Assert
        Assert.Null(ValueOf(report));
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task EmptyCell_IntoInt_IsAMissingValue(
        TabularType type
    )
    {
        // Act
        var report = await ReadAsync<int>(type, "");

        // Assert
        Assert.Equal(TabularErrorCodes.Conversion.MissingValue, ErrorOf(report));
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task EmptyCell_WithEmptyStringAsNull_IsNull(
        TabularType type
    )
    {
        // Act
        var report = await ReadAsync(type, "", Map<string?>(c => c.EmptyStringAsNull()));

        // Assert
        Assert.Null(ValueOf(report));
    }

    [Fact]
    public async Task NullText_DistinguishesNullFromEmpty()
    {
        // Arrange
        var map = Map<string?>(c => c.NullText("NULL"));

        // Act
        // WHY: The round trip of the own output is the checked contract, so export and reimport form one action.
        var (_, file) =
            await CsvInstance.ExportAsync(new[] { new Value<string?>(null), new Value<string?>("") }, map, Token);

        var report = await CsvInstance.ImportAsync(file!, map, Token);

        // Assert
        Assert.Equal([null, ""], report.Items!.Select(item => item.Item));
    }

    [Fact]
    public async Task NullText_EqualToARealValue_FailsTheExport()
    {
        // Act
        var report = await CsvInstance.ExportAsync(new[] { new Value<string?>("NULL") },
            Map<string?>(c => c.NullText("NULL")),
            new MemoryStream(),
            Token);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    [Theory]
    [InlineData("d93f16aa-3667-4b95-a814-d4275dbd0554")]
    [InlineData("D93F16AA36674B95A814D4275DBD0554")]
    [InlineData("{d93f16aa-3667-4b95-a814-d4275dbd0554}")]
    [InlineData("(d93f16aa-3667-4b95-a814-d4275dbd0554)")]
    [InlineData("{0xd93f16aa,0x3667,0x4b95,{0xa8,0x14,0xd4,0x27,0x5d,0xbd,0x05,0x54}}")]
    public async Task Guid_AcceptsEveryStandardFormat(
        string text
    )
    {
        // Act
        var report = await ReadAsync<Guid>(TabularType.Csv, text);

        // Assert
        Assert.Equal(Guid.Parse("d93f16aa-3667-4b95-a814-d4275dbd0554"), ValueOf(report));
    }

    [Fact]
    public async Task Guid_IsExportedLowercaseD()
    {
        // Arrange
        var value = Guid.Parse("D93F16AA36674B95A814D4275DBD0554");

        // Act
        var (_, file) = await CsvInstance.ExportAsync(new[] { new Value<Guid>(value), new Value<Guid>(Guid.Empty) },
            Map<Guid>(),
            Token);

        // Assert
        Assert.Equal([["d93f16aa-3667-4b95-a814-d4275dbd0554"], ["00000000-0000-0000-0000-000000000000"]],
            Cells(TabularType.Csv, file!));
    }

    [Fact]
    public async Task Rune_KeepsAnEmoji()
    {
        // Act
        var report = await ReadAsync<Rune>(TabularType.Csv, "😀");

        // Assert
        Assert.Equal(new Rune(0x1F600), ValueOf(report));
    }

    [Fact]
    public async Task Char_RejectsAnEmoji()
    {
        // Act
        var report = await ReadAsync<char>(TabularType.Csv, "😀");

        // Assert
        Assert.NotEmpty(Codes(report));
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task CustomConversion_ReadsThroughTheBaseTypeAndAssignedExceptionsRejectTheRow(
        TabularType type
    )
    {
        // Act
        var report = await Import.ImportAsync(Create(type, ["K-1"], ["X-2"], ["K-3"]),
            type,
            s_customers,
            new TabularImportOptions { Strategy = ImportStrategy.Partial },
            Token);

        // Assert
        Assert.Equal(["K-1", "K-3"], report.Items!.Select(item => item.Number.Text));
        Assert.Equal("customer.invalid",
            Assert.Single(report.Errors)
                .Code.Value);
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task CustomConversion_WritesThroughTheBaseType(
        TabularType type
    )
    {
        // Arrange
        var rows = new[] { new Customer(new CustomerNumber("K-1")), new Customer(new CustomerNumber("K-3")) };

        // Act
        var (_, file) = await Export.ExportAsync(rows, s_customers, type, Token);

        // Assert
        Assert.Equal([["K-1"], ["K-3"]], Cells(type, file!));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("1", true)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData("0", false)]
    public async Task Bool_AcceptsTheStandardTexts(
        string text,
        bool expected
    )
    {
        // Act
        var report = await ReadAsync<bool>(TabularType.Csv, text, culture: CultureInfo.GetCultureInfo("de-DE"));

        // Assert
        Assert.Equal(expected, ValueOf(report));
    }

    [Theory]
    [InlineData("2")]
    [InlineData("-1")]
    [InlineData("Ja")]
    [InlineData("")]
    public async Task Bool_RejectsOtherTextsAndEmpty(
        string text
    )
    {
        // Act
        var report = await ReadAsync<bool>(TabularType.Csv, text, culture: CultureInfo.GetCultureInfo("de-DE"));

        // Assert
        Assert.NotEmpty(Codes(report));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(0, false)]
    public async Task Bool_ExcelCell_IsReadNatively(
        object cell,
        bool expected
    )
    {
        // Act
        var report = await ReadAsync<bool>(TabularType.Excel, cell);

        // Assert
        Assert.Equal(expected, ValueOf(report));
    }

    [Fact]
    public async Task NullableBool_EmptyExcelCell_IsNull()
    {
        // Act
        var report = await ReadAsync<bool?>(TabularType.Excel, "");

        // Assert
        Assert.Null(ValueOf(report));
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task Bool_ReadsBackWithTheStandardTexts(
        TabularType type
    )
    {
        // Act
        // WHY: The round trip of the own output is the checked contract, so export and reimport form one action.
        var (_, file) = await Export.ExportAsync(s_flagRows, s_flags, type, Token);
        var report = await Import.ImportAsync(file!, type, s_flags, Token);

        // Assert
        Assert.Equal(s_flagRows, report.Items);
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task Bool_ReadsBackWithOwnTexts(
        TabularType type
    )
    {
        // Act
        // WHY: The round trip of the own output is the checked contract, so export and reimport form one action.
        var (_, file) = await Export.ExportAsync(s_flagRows, s_ownFlagTexts, type, Token);
        var report = await Import.ImportAsync(file!, type, s_ownFlagTexts, Token);

        // Assert
        Assert.Equal(s_flagRows, report.Items);
    }

    [Fact]
    public async Task Bool_IsWrittenAsABooleanExcelCell()
    {
        // Act
        var (_, file) = await Export.ExportAsync(s_flagRows, s_flags, TabularType.Excel, Token);

        // Assert
        Assert.Equal("b", CellTypes(file!, 1)[1]);
    }

    [Fact]
    public async Task Bool_IsWrittenWithTheStandardCsvTexts()
    {
        // Act
        var (_, file) = await Export.ExportAsync(s_flagRows, s_flags, TabularType.Csv, Token);

        // Assert
        Assert.Equal([["1", "true"], ["2", "false"], ["3", ""]], Cells(TabularType.Csv, file!));
    }

    [Fact]
    public async Task Bool_IsWrittenWithOwnCsvTexts()
    {
        // Act
        var (_, file) = await Export.ExportAsync(s_flagRows, s_ownFlagTexts, TabularType.Csv, Token);

        // Assert
        Assert.Equal([["1", "Ja"], ["2", "Nein"], ["3", ""]], Cells(TabularType.Csv, file!));
    }

    [Theory]
    [InlineData("false", null)]
    [InlineData("0", null)]
    [InlineData(null, "TRUE")]
    [InlineData(null, "1")]
    [InlineData("Ja", "ja")]
    public void Bool_OwnTextEqualToATextOfTheOtherValue_IsAConfigurationError(
        string? trueText,
        string? falseText
    )
    {
        // Act
        var exception = Record.Exception(() => TabularMap
            .For<Flag>()
            .Column(x => x.Key, index: 0)
            .Column(x => x.Value,
                index: 1,
                configure: c =>
                {
                    if (trueText is not null)
                    {
                        c.TrueText(trueText);
                    }

                    if (falseText is not null)
                    {
                        c.FalseText(falseText);
                    }
                })
            .Build());

        // Assert
        Assert.IsType<TabularConfigurationException>(exception);
    }

    [Fact]
    public void Bool_TextEqualToATextOfTheOtherValueAfterTheTrimOfTheField_FailsAtBuild()
    {
        // Act
        var exception = Record.Exception(() => TabularMap
            .For<Flag>()
            .Column(x => x.Key, index: 0)
            .Column(x => x.Value,
                index: 1,
                configure: c => c
                    .TrueText(" false ")
                    .Trim())
            .Build());

        // Assert
        Assert.IsType<TabularConfigurationException>(exception);
    }

    [Fact]
    public async Task Bool_TextEqualToATextOfTheOtherValueAfterTheTrimOfTheCall_FailsBeforeTheFirstRow()
    {
        // Arrange
        var map = TabularMap
            .For<Flag>()
            .Column(x => x.Key, index: 0)
            .Column(x => x.Value, index: 1, configure: c => c.TrueText(" false "))
            .Build();

        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8("1,true"),
            map,
            new CsvImportOptions { TrimFields = true },
            Token);

        // Assert
        Assert.Equal((ReportStatus.Failed, 0L, TabularErrorKind.Configuration),
            (report.Status, report.RowsRead, report.Errors[0].Kind));
    }

    [Fact]
    public void Bool_ConversionCollision_AfterTheTrimOfTheField_FailsAtBuild()
    {
        // Act
        var exception = Record.Exception(() => TabularMap
            .For<Flag>()
            .Column(x => x.Key, index: 0)
            .Column(x => x.Value,
                index: 1,
                configure: c => c
                    .Trim()
                    .Convert(value => value, value => value, configure: b => b.TrueText(" false ")))
            .Build());

        // Assert
        Assert.IsType<TabularConfigurationException>(exception);
    }

    [Fact]
    public void Bool_ConversionTrim_WithoutTheTrimOfTheField_Builds()
    {
        // Act
        var exception = Record.Exception(() => TabularMap
            .For<Flag>()
            .Column(x => x.Key, index: 0)
            .Column(x => x.Value,
                index: 1,
                configure: c => c
                    .Trim(false)
                    .Convert(value => value,
                        value => value,
                        configure: b => b
                            .Trim()
                            .TrueText(" false ")))
            .Build());

        // Assert
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("X", "X")]
    [InlineData("false", "Nein")]
    public async Task Bool_TranslationEqualToATextOfTheOtherValue_FailsTheExportBeforeTheFirstRow(
        string trueTranslation,
        string falseTranslation
    )
    {
        // Act
        var report = await CsvInstance.ExportAsync(new[] { new Flag(1, false) },
            s_translatedFlags,
            new MemoryStream(),
            new CsvExportOptions { Translations = FlagTranslations(trueTranslation, falseTranslation) },
            Token);

        // Assert
        Assert.Equal((ReportStatus.Failed, 0L, TabularErrorKind.Configuration),
            (report.Status, report.RowsRead, report.Errors[0].Kind));
    }

    [Theory]
    [InlineData("X", "X")]
    [InlineData("false", "Nein")]
    public async Task Bool_TranslationEqualToATextOfTheOtherValue_FailsTheImportBeforeTheFirstRow(
        string trueTranslation,
        string falseTranslation
    )
    {
        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8("1,X"),
            s_translatedFlags,
            new CsvImportOptions { Translations = FlagTranslations(trueTranslation, falseTranslation) },
            Token);

        // Assert
        Assert.Equal((ReportStatus.Failed, 0L, TabularErrorKind.Configuration),
            (report.Status, report.RowsRead, report.Errors[0].Kind));
    }

    private static Dictionary<string, string> FlagTranslations(
        string trueTranslation,
        string falseTranslation
    ) => new()
    {
        ["t"] = trueTranslation,
        ["f"] = falseTranslation,
    };

    private sealed record Value<T>(T Item);

    private sealed record CustomerNumber(string Text);

    private sealed record Flag(
        int Key,
        bool? Value
    );

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    private sealed record Customer(CustomerNumber Number);

    private sealed class InvalidCustomerNumberException : Exception
    {
        public InvalidCustomerNumberException()
            : base("invalid") { }
    }
}
