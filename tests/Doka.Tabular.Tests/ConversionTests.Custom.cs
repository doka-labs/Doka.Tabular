using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Tests;

/// <summary>Custom conversions over nullable and string base types, base settings, and empty text.</summary>
public sealed partial class ConversionTests
{
    private static readonly TabularErrorCode s_invalidCode = TabularErrorCode.Create("probe.invalid");

    private static TabularMap<Keyed<Wrapped>> ValidatedWrapped => KeyedMap<Wrapped>(c
        => c.Convert<string, Wrapped>(
            text => new Wrapped(text),
            wrapped => wrapped.Text,
            configure: b => b
                .Trim()
                .Validate(text => text != "bad", s_invalidCode)));

    private static TabularMap<Keyed<T>> KeyedMap<T>(
        Action<TabularColumnBuilder<T>> configure,
        Action<TabularMapBuilder<Keyed<T>>>? map = null
    )
    {
        var builder = TabularMap
            .For<Keyed<T>>()
            .Column(x => x.Key, index: 0)
            .Column(x => x.Item, index: 1, configure: configure);

        map?.Invoke(builder);

        return builder.Build();
    }

    [Fact]
    public async Task CustomConversionOverANullableBase_Imports()
    {
        // Arrange
        var map = KeyedMap<Wrapped>(c => c.Convert<int?, Wrapped>(
            number => new Wrapped($"{number}"),
            wrapped => int.Parse(wrapped.Text, CultureInfo.InvariantCulture)));

        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8("1,42"), map, Token);

        // Assert
        Assert.Equal(
            "42",
            Assert.Single(report.Items!)
                .Item.Text);
    }

    [Fact]
    public async Task NullBaseValue_ReachesTheCustomRead()
    {
        // Arrange
        var map = KeyedMap<Wrapped>(c => c.Convert<string?, Wrapped>(
            text => new Wrapped(text ?? "empty"),
            wrapped => wrapped.Text,
            configure: b => b.EmptyStringAsNull()));

        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8("1,"), map, Token);

        // Assert
        Assert.Equal(
            "empty",
            Assert.Single(report.Items!)
                .Item.Text);
    }

    [Fact]
    public async Task ExpectedExceptionOfTheCustomRead_ForEmptyText_IsARowError()
    {
        // Arrange
        var map = KeyedMap<Wrapped>(
            c => c.Convert<string, Wrapped>(
                text => text.Length == 0
                    ? throw new InvalidCustomerNumberException()
                    : new Wrapped(text),
                wrapped => wrapped.Text),
            builder => builder.Expect<InvalidCustomerNumberException>(s_invalidCode));

        // Act
        var report = await CsvInstance.ImportAsync(
            TestInput.Utf8("1,\n2,ok"),
            map,
            new CsvImportOptions { Strategy = ImportStrategy.Partial },
            Token);

        // Assert
        Assert.Null(report.FatalError);
        Assert.Equal(
            s_invalidCode,
            Assert.Single(report.Errors)
                .Code);

        Assert.Equal(
            "ok",
            Assert.Single(report.Items!)
                .Item.Text);
    }

    [Fact]
    public async Task BaseSettings_TrimAndValidate_RejectAnInvalidText()
    {
        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8("1, bad "), ValidatedWrapped, Token);

        // Assert
        Assert.Equal(
            s_invalidCode,
            Assert.Single(report.Errors)
                .Code);
    }

    [Fact]
    public async Task BaseSettings_TrimAndValidate_ReadAValidText()
    {
        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8("1, good "), ValidatedWrapped, Token);

        // Assert
        Assert.Equal(
            "good",
            Assert.Single(report.Items!)
                .Item.Text);
    }

    [Fact]
    public async Task BaseSettings_Required_RejectsAnEmptyField()
    {
        // Arrange
        var required = KeyedMap<Wrapped>(c => c.Convert<string, Wrapped>(
            text => new Wrapped(text),
            wrapped => wrapped.Text,
            configure: b => b.Required()));

        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8("1,"), required, Token);

        // Assert
        Assert.Equal(
            TabularErrorCodes.Conversion.MissingValue,
            Assert.Single(report.Errors)
                .Code);
    }

    [Fact]
    public async Task BaseNullText_GivesTheCustomReadANullBase()
    {
        // Arrange
        var map = KeyedMap<Wrapped>(c => c.Convert<string?, Wrapped>(
            text => new Wrapped(text ?? "empty"),
            wrapped => wrapped.Text,
            configure: b => b.NullText("NULL")));

        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8("1,NULL"), map, Token);

        // Assert
        Assert.Equal(
            "empty",
            Assert.Single(report.Items!)
                .Item.Text);
    }

    [Fact]
    public async Task BaseExportRule_ChecksTheWrittenBaseValue()
    {
        // Arrange
        var map = KeyedMap<Wrapped>(c => c.Convert(
            number => new Wrapped($"{number}"),
            _ => -1,
            configure: b => b.Validate(number => number > 0, s_invalidCode, alsoOnExport: true)));

        // Act
        var report = await CsvInstance.ExportAsync(
            new[] { new Keyed<Wrapped>(1, new Wrapped("x")) },
            map,
            new MemoryStream(),
            Token);

        // Assert
        Assert.Equal(s_invalidCode, (report.FatalError ?? Assert.Single(report.Errors)).Code);
    }

    [Fact]
    public async Task BaseExportRule_ChecksTheValueThatIsWritten()
    {
        // Arrange
        var calls = 0;
        var map = KeyedMap<Wrapped>(c => c.Convert(
            number => new Wrapped($"{number}"),
            _ => ++calls == 1
                ? 1
                : -1,
            configure: b => b.Validate(number => number > 0, s_invalidCode, alsoOnExport: true)));

        // Act
        var (report, file) = await CsvInstance.ExportAsync(
            new[] { new Keyed<Wrapped>(1, new Wrapped("x")) },
            map,
            Token);

        // Assert
        Assert.Equal((ReportStatus.Succeeded, 1), (report.Status, calls));
        Assert.Equal("1,1", await new StreamReader(file!).ReadToEndAsync(Token));
    }

    [Fact]
    public async Task ThrowingWriteFunction_IsATechnicalError()
    {
        // Arrange
        var calls = 0;
        var map = KeyedMap<Wrapped>(c => c.Convert(
            number => new Wrapped($"{number}"),
            _ => ++calls == 1
                ? throw new InvalidOperationException("write failed")
                : 1,
            configure: b => b.Validate(number => number > 0, s_invalidCode, alsoOnExport: true)));

        // Act
        var report = await CsvInstance.ExportAsync(
            new[] { new Keyed<Wrapped>(1, new Wrapped("x")) },
            map,
            new MemoryStream(),
            Token);

        // Assert
        var error = report.FatalError ?? Assert.Single(report.Errors);
        Assert.Equal((TabularErrorCodes.Callback.Failed, TabularErrorKind.Technical), (error.Code, error.Kind));
    }

    [Fact]
    public async Task BaseExportRule_ChecksANullBaseValue()
    {
        // Arrange
        var map = KeyedMap<Wrapped>(c => c.Convert<int?, Wrapped>(
            number => new Wrapped($"{number}"),
            _ => null,
            configure: b => b.Validate(number => number is not null, s_invalidCode, alsoOnExport: true)));

        // Act
        var report = await CsvInstance.ExportAsync(
            new[] { new Keyed<Wrapped>(1, new Wrapped("x")) },
            map,
            new MemoryStream(),
            Token);

        // Assert
        Assert.Equal(s_invalidCode, (report.FatalError ?? Assert.Single(report.Errors)).Code);
    }

    [Fact]
    public async Task BaseNullText_IsWrittenForANullBaseValue()
    {
        // Arrange
        var map = KeyedMap<Wrapped>(c => c.Convert<string?, Wrapped>(
            text => new Wrapped(text ?? "empty"),
            _ => null,
            configure: b => b.NullText("NULL")));

        // Act
        var (report, file) = await CsvInstance.ExportAsync(
            new[] { new Keyed<Wrapped>(1, new Wrapped("x")) },
            map,
            Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        Assert.Equal("1,NULL", await new StreamReader(file!).ReadToEndAsync(Token));
    }

    [Fact]
    public void NullTextOfOneLevelWithEmptyStringAsNullOfTheOther_IsAConfigurationError()
    {
        // Act
        var exception = Record.Exception(() => KeyedMap<string?>(c => c
            .EmptyStringAsNull()
            .Convert<string?, string?>(text => text, text => text, configure: b => b.NullText("NULL"))));

        // Assert
        Assert.IsType<TabularConfigurationException>(exception);
    }

    [Fact]
    public async Task BaseValueEqualToTheTranslatedNullMarker_IsRejectedOnExport()
    {
        // Arrange
        var map = KeyedMap<Wrapped>(c => c.Convert<string?, Wrapped>(
            text => new Wrapped(text ?? "empty"),
            wrapped => wrapped.Text,
            configure: b => b.EmptyTranslationKey("empty")));

        // Act
        var report = await CsvInstance.ExportAsync(
            new[] { new Keyed<Wrapped>(1, new Wrapped("NULL")) },
            map,
            new MemoryStream(),
            new CsvExportOptions { Translations = new Dictionary<string, string> { ["empty"] = "NULL" } },
            Token);

        // Assert
        Assert.Equal(
            TabularErrorCodes.Conversion.InvalidFormat,
            (report.FatalError ?? Assert.Single(report.Errors)).Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValueEqualToTheNullMarkerAfterTrimming_IsRejectedOnExport(
        bool translated
    )
    {
        // Arrange
        var map = KeyedMap<Wrapped>(c => c.Convert<string?, Wrapped>(
            text => new Wrapped(text ?? "empty"),
            wrapped => wrapped.Text,
            configure: b =>
            {
                b.Trim();
                if (translated)
                {
                    b.EmptyTranslationKey("empty");
                }
                else
                {
                    b.NullText("NULL");
                }
            }));

        // Act
        var report = await CsvInstance.ExportAsync(
            new[] { new Keyed<Wrapped>(1, new Wrapped(" NULL ")) },
            map,
            new MemoryStream(),
            new CsvExportOptions { Translations = new Dictionary<string, string> { ["empty"] = "NULL" } },
            Token);

        // Assert
        Assert.Equal(
            TabularErrorCodes.Conversion.InvalidFormat,
            (report.FatalError ?? Assert.Single(report.Errors)).Code);
    }

    [Fact]
    public void BaseNullTextWithEmptyStringAsNull_IsAConfigurationError()
    {
        // Act
        var exception = Record.Exception(() => KeyedMap<Wrapped>(c => c.Convert<string?, Wrapped>(
            text => new Wrapped(text ?? "empty"),
            wrapped => wrapped.Text,
            configure: b => b
                .NullText("NULL")
                .EmptyStringAsNull())));

        // Assert
        Assert.IsType<TabularConfigurationException>(exception);
    }

    [Fact]
    public async Task PadLeft_AppliesToEmptyTextOnImport()
    {
        // Arrange
        var map = KeyedMap<string>(c => c.PadLeft(5, '0'));

        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8("1,"), map, Token);

        // Assert
        Assert.Equal(
            "00000",
            Assert.Single(report.Items!)
                .Item);
    }

    private sealed record Keyed<T>(
        int Key,
        T Item
    );

    private sealed record Wrapped(string Text);
}
