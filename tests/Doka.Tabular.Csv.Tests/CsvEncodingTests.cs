namespace Doka.Tabular.Csv.Tests;

/// <summary>Encodings, byte order mark, and the container check before the first record.</summary>
public sealed class CsvEncodingTests
{
    private static readonly TabularMap<One> s_one = TabularMap
        .For<One>()
        .Column(x => x.Value, index: 0)
        .Build();

    private static readonly CsvFormat s_format = new(new CsvFormatOptions());

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Import_Windows1252Bytes_ReadAsTextWithTheChosenEncoding()
    {
        // Arrange
        var bytes = TestInput.Bytes(0x4D, 0xFC, 0x6C, 0x6C, 0x65, 0x72);

        // Act
        var report =
            await s_format.ImportAsync(bytes, s_one, new CsvImportOptions { Encoding = Encoding.Latin1 }, Token);

        // Assert
        Assert.Equal(
            "Müller",
            Assert.Single(report.Items!)
                .Value);
    }

    [Fact]
    public async Task Import_Windows1252Bytes_FailUnderUtf8()
    {
        // Arrange
        var bytes = TestInput.Bytes(0x4D, 0xFC, 0x6C, 0x6C, 0x65, 0x72);

        // Act
        var report = await s_format.ImportAsync(bytes, s_one, Token);

        // Assert
        Assert.Equal(
            TabularErrorCodes.Parsing.InvalidEncoding,
            (report.FatalError ?? Assert.Single(report.Errors)).Code);
    }

    [Fact]
    public async Task Export_Utf8_WritesMultiByteSequences()
    {
        // Arrange
        var rows = new[] { new One("Müller"), new One("李") };

        // Act
        var (_, file) = await s_format.ExportAsync(rows, s_one, Token);

        // Assert
        await using var stream = file!;
        Assert.Equal("4D-C3-BC-6C-6C-65-72-0D-0A-E6-9D-8E", BitConverter.ToString(TestInput.AllBytes(stream)));
    }

    [Fact]
    public async Task Export_CharacterOutsideTheEncoding_FailsWithItsLocation()
    {
        // Act
        var report = await s_format.ExportAsync(
            new[] { new One("李") },
            s_one,
            new MemoryStream(),
            new CsvExportOptions { Encoding = Encoding.Latin1 },
            Token);

        // Assert
        var error = report.FatalError ?? Assert.Single(report.Errors);
        Assert.Equal(TabularErrorCodes.Parsing.InvalidEncoding, error.Code);
        Assert.Equal((1L, 0), (error.Location.RowNumber!.Value, error.Location.ColumnIndex!.Value));
    }

    [Theory]
    [InlineData(new byte[] { 0x41, 0x64, 0x61 })]
    [InlineData(
        new byte[]
        {
            0xEF,
            0xBB,
            0xBF,
            0x41,
            0x64,
            0x61,
        })]
    public async Task Import_Utf8WithAndWithoutBom_ReadsTheSameFirstValue(
        byte[] bytes
    )
    {
        // Arrange
        var map = TabularMap
            .For<One>()
            .Column(
                x => x.Value,
                index: 0,
                configure: c => c.Validate(value => value == "Ada", code: TabularErrorCode.Create("test.ada")))
            .Build();

        // Act
        var report = await s_format.ImportAsync(new MemoryStream(bytes), map, Token);

        // Assert
        Assert.Equal(
            "Ada",
            Assert.Single(report.Items!)
                .Value);
    }

    [Fact]
    public async Task Import_ScalarSplitAtAFullBuffer_KeepsEveryCharacter()
    {
        // Arrange
        // The first eight bytes end inside a four-byte scalar; the following full block then decodes to one
        // character more than the character buffer holds.
        List<byte> bytes = [.. "abcde\U0001F600"u8, .. Enumerable.Repeat((byte)'x', 32766), .. "\nBob"u8];

        // Act
        var report = await s_format.ImportAsync(new MemoryStream(bytes.ToArray()), s_one, Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        Assert.Equal(["abcde\U0001F600" + new string('x', 32766), "Bob"], report.Items!.Select(x => x.Value));
    }

    [Fact]
    public async Task Import_LaterByteOrderMark_StaysFieldContent()
    {
        // Act
        var report = await s_format.ImportAsync(TestInput.Utf8("Ada\n﻿Bob"), s_one, Token);

        // Assert
        Assert.Equal("﻿Bob", report.Items![1].Value);
    }

    [Theory]
    [InlineData(false, "41")]
    [InlineData(true, "EF-BB-BF-41")]
    public async Task Export_WritesTheBomOnlyWhenRequested(
        bool bom,
        string prefix
    )
    {
        // Act
        var (_, file) = await s_format.ExportAsync(
            new[] { new One("Ada") },
            s_one,
            new CsvExportOptions { WriteBom = bom },
            Token);

        // Assert
        await using var stream = file!;
        Assert.StartsWith(prefix + "-", BitConverter.ToString(TestInput.AllBytes(stream)), StringComparison.Ordinal);
    }

    public static TheoryData<string, string, bool> ContainerSignatures()
    {
        var data = new TheoryData<string, string, bool>();
        foreach (var signature in new[] { "50-4B-03-04-14-00", "D0-CF-11-E0-A1-B1-1A-E1" })
        {
            foreach (var encoding in new[] { "utf-8", "iso-8859-1" })
            {
                data.Add(signature, encoding, false);
                data.Add(signature, encoding, true);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ContainerSignatures))]
    public async Task Import_ContainerSignature_IsAFormatMismatchBeforeTheFirstRecord(
        string signature,
        string encoding,
        bool forwardOnly
    )
    {
        // Arrange
        var bytes = Convert.FromHexString(signature.Replace("-", "", StringComparison.Ordinal));
        Stream input = forwardOnly
            ? new ForwardOnlyStream(new MemoryStream(bytes))
            : new MemoryStream(bytes);

        // Act
        var report = await s_format.ImportAsync(
            input,
            s_one,
            new CsvImportOptions
            {
                Strategy = ImportStrategy.Partial,
                Encoding = Encoding.GetEncoding(encoding),
            },
            Token);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
        Assert.Equal(
            TabularErrorCodes.Schema.FormatMismatch,
            Assert.Single(report.Errors)
                .Code);

        Assert.Empty(report.Items!);
    }

    private sealed record One(string Value);
}
