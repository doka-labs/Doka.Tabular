using System.Numerics;
using System.Text.Json.Serialization;
using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Tests;

/// <summary>
/// The own output reads back: every successful export of every type and setting, in every culture, output form, and
/// trim rule, imports to the same values; an export that cannot keep a value fails with a report instead.
/// </summary>
public sealed class RoundTripMatrixTests
{
    private static readonly string[] s_cultures =
        ["", "de-DE", "fr-FR", "sv-SE", "de-CH", "hi-IN", "ar-SA", "fa-IR", "he-IL", "ja-JP"];

    public static TheoryData<string, string, string, bool> Cases()
    {
        var data = new TheoryData<string, string, string, bool>();
        foreach (var entry in AllCases())
        {
            foreach (var culture in s_cultures)
            {
                foreach (var form in new[] { Form.Csv, Form.ExcelNative, Form.ExcelText })
                {
                    data.Add(entry.Name, culture, form.ToString(), false);
                    data.Add(entry.Name, culture, form.ToString(), true);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task OwnOutput_ReadsBackInEveryCultureFormAndTrim(
        string name,
        string culture,
        string form,
        bool trims
    )
    {
        // Arrange
        var entry = AllCases()
            .Single(candidate => candidate.Name == name);

        // Act
        var problem = await entry.Run(Enum.Parse<Form>(form), CultureInfo.GetCultureInfo(culture), trims);

        // Assert
        Assert.Null(problem);
    }

    private static IEnumerable<Case> AllCases()
    {
        yield return Case.Of("int", [0, -1, 1234567, int.MaxValue, int.MinValue]);
        yield return Case.Of("int percent", [0, -1, 12345], c => c.TextStyle(NumberTextStyle.Percent));
        yield return Case.Of("int currency", [0, -1, 12345], c => c.TextStyle(NumberTextStyle.Currency));
        yield return Case.Of("long", [long.MaxValue, long.MinValue, 0L]);
        yield return Case.Of("ulong", [ulong.MaxValue, 0UL]);
        yield return Case.Of("sbyte", [sbyte.MinValue, sbyte.MaxValue]);
        yield return Case.Of("Int128", [Int128.MaxValue, Int128.MinValue]);
        yield return Case.Of("UInt128", [UInt128.MaxValue]);
        yield return Case.Of("BigInteger", [BigInteger.Pow(10, 40), -BigInteger.Pow(7, 30), BigInteger.Zero]);
        yield return Case.Of("decimal", [0.1m, -1234567.891m, decimal.MaxValue, decimal.MinValue, 0.0000000001m]);
        yield return Case.Of("decimal percent", [0.125m, -1.5m], c => c.TextStyle(NumberTextStyle.Percent));
        yield return Case.Of("decimal currency", [12.5m, -1234567.891m], c => c.TextStyle(NumberTextStyle.Currency));
        yield return Case.Of("decimal N2", [12.5m, -1234567.89m], c => c.ExportFormat("N2"));
        yield return Case.Of("decimal rounded", [12.345m, -0.005m], c => c.Rounding(2), [12.34m, 0m]);
        yield return Case.Of("double", [0.1, -1.5e-7, 1e21, double.MaxValue, double.Epsilon, 1.0 / 3]);
        yield return Case.Of("double exponent", [1e21, -2.5e-30], c => c.AllowExponent());
        yield return Case.Of("double percent", [0.125, -1.5], c => c.TextStyle(NumberTextStyle.Percent));
        yield return Case.Of(
            "double non-finite",
            [double.NaN, double.PositiveInfinity, double.NegativeInfinity, 1.5],
            c => c.NonFiniteValues());

        yield return Case.Of("float", [0.1f, float.MaxValue, -3.4e-38f]);
        yield return Case.Of("Half", [(Half)0.1, Half.MaxValue, Half.Epsilon]);
        yield return Case.Of("bool", [true, false]);
        yield return Case.Of(
            "bool texts",
            [true, false],
            c => c
                .TrueText("ja")
                .FalseText("nein"));

        yield return Case.Of("string", ["Ada", "a,b", "\"quoted\"", "line\nbreak", "=1+1", "Müller 李"]);

        // WHY: PadLeft also pads the text it reads, so a short value reads back padded.
        yield return Case.Of("string padded", ["7", "1234"], c => c.PadLeft(4, '0'), ["0007", "1234"]);
        yield return Case.Of("char", ['a', ',', '"', 'ü']);
        yield return Case.Of("Rune", [new Rune('a'), new Rune(0x1F600)]);
        yield return Case.Of("Guid", [Guid.Empty, new Guid("3f2504e0-4f89-11d3-9a0c-0305e82c3301")]);
        yield return Case.Of(
            "Guid braces",
            [new Guid("3f2504e0-4f89-11d3-9a0c-0305e82c3301")],
            c => c
                .GuidExportFormat("B")
                .AcceptGuidFormats("B"));

        yield return Case.Of("enum", [State.Open, State.InWork, State.Closed]);
        yield return Case.Of(
            "enum number",
            [State.Open, State.Closed],
            c => c
                .ExportAsNumber()
                .AllowNumericInput());

        yield return Case.Of("enum member name", [State.InWork], c => c.ExportMemberName());
        yield return Case.Of("flags", [Rights.None, Rights.Read, Rights.Read | Rights.Write | Rights.Delete]);
        yield return Case.Of("flags separator", [Rights.Read | Rights.Delete], c => c.FlagsSeparator(';'));
        yield return Case.Of("DateOnly", [new DateOnly(2024, 2, 29), DateOnly.MinValue, DateOnly.MaxValue]);
        yield return Case.Of("DateOnly format", [new DateOnly(2024, 2, 29)], c => c.Format("dd.MM.yyyy"));
        yield return Case.Of(
            "TimeOnly",
            [new TimeOnly(0, 0), new TimeOnly(23, 59, 59, 999).Add(TimeSpan.FromTicks(9999))]);

        yield return Case.Of("TimeOnly format", [new TimeOnly(14, 30)], c => c.Format("HH:mm"));
        yield return Case.Of("TimeOnly 12h", [new TimeOnly(1, 30), new TimeOnly(13, 30)], c => c.Format("hh:mm tt"));
        yield return Case.Of(
            "DateTime",
            [
                new DateTime(
                    2024,
                    2,
                    29,
                    14,
                    30,
                    15,
                    123).AddTicks(4567),
                new DateTime(1900, 1, 1), new DateTime(9999, 12, 31),
            ]);

        yield return Case.Of(
            "DateTime format",
            [new DateTime(2024, 2, 29, 14, 30, 0)],
            c => c.Format("yyyy-MM-dd HH:mm"));

        yield return Case.Of(
            "DateTime rounded",
            [
                new DateTime(
                    2024,
                    2,
                    29,
                    14,
                    30,
                    15,
                    600),
            ],
            c => c.Rounding(TimeSpan.FromSeconds(1)),
            [new DateTime(2024, 2, 29, 14, 30, 16)]);

        yield return Case.Of(
            "DateTime UTC",
            [
                new DateTime(
                    2024,
                    2,
                    29,
                    14,
                    30,
                    0,
                    DateTimeKind.Utc),
            ],
            c => c.ExcelAsUtc());

        yield return Case.Of(
            "DateTime file zone",
            [
                new DateTime(
                    2024,
                    7,
                    1,
                    12,
                    0,
                    0,
                    DateTimeKind.Utc),
                new DateTime(
                    2024,
                    1,
                    1,
                    12,
                    0,
                    0,
                    DateTimeKind.Utc),
            ],
            c => c.FileTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")));

        yield return Case.Of(
            "DateTimeOffset",
            [
                new DateTimeOffset(
                    2024,
                    2,
                    29,
                    14,
                    30,
                    0,
                    TimeSpan.FromHours(2)),
                DateTimeOffset.MinValue, DateTimeOffset.MaxValue,
            ]);

        yield return Case.Of(
            "DateTimeOffset format",
            [
                new DateTimeOffset(
                    2024,
                    2,
                    29,
                    14,
                    30,
                    0,
                    TimeSpan.FromHours(-5)),
            ],
            c => c.Format("yyyy-MM-dd HH:mm:ss zzz"));

        yield return Case.Of(
            "DateTimeOffset file zone",
            [
                new DateTimeOffset(
                    2024,
                    7,
                    1,
                    12,
                    0,
                    0,
                    TimeSpan.FromHours(2)),
            ],
            c => c.FileTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")));

        yield return Case.Of("DateOnly short", [new DateOnly(2024, 2, 29)], c => c.Format("d"));
        yield return Case.Of("DateTime general", [new DateTime(2024, 2, 29, 14, 30, 15)], c => c.Format("G"));
        yield return Case.Of("TimeOnly short", [new TimeOnly(14, 30)], c => c.Format("t"));
        yield return Case.Of(
            "decimal C",
            [12.5m, -1234.5m],
            c => c
                .TextStyle(NumberTextStyle.Currency)
                .ExportFormat("C")
                .Rounding(2));

        yield return Case.Of("float non-finite", [float.NaN, float.NegativeInfinity], c => c.NonFiniteValues());
        yield return Case.Of(
            "TimeSpan",
            [TimeSpan.Zero, TimeSpan.FromHours(27), TimeSpan.FromTicks(-1234567), TimeSpan.MaxValue]);

        yield return Case.Of(
            "TimeSpan hours",
            [TimeSpan.FromHours(27), TimeSpan.FromMinutes(-90)],
            c => c.ExcelDuration(ExcelDurationUnit.Hours));

        yield return Case.Of("TimeSpan format", [TimeSpan.FromMinutes(90)], c => c.Format("hh\\:mm"));
        yield return Case.Of<int?>("int?", [1, null]);
        yield return Case.Of("string? null text", ["Ada", null, ""], c => c.NullText("-"));
        yield return Case.Of<DateOnly?>("DateOnly?", [new DateOnly(2024, 1, 1), null]);
        yield return Case.Of(
            "custom",
            [new Code("A-1"), new Code("x")],
            c => c.Convert(text => new Code(text), code => code.Text));
    }

    private enum Form
    {
        Csv,
        ExcelNative,
        ExcelText,
    }

    [Flags]
    private enum Rights
    {
        None = 0,
        Read = 1,
        Write = 2,
        Delete = 4,
    }

    private enum State
    {
        Open = 1,

        [JsonStringEnumMemberName("in work")]
        InWork = 2,
        Closed = 3,
    }

    private sealed record Code(string Text);

    private sealed record Cell<T>(
        int Key,
        T Item
    );

    private sealed record Case(
        string Name,
        Func<Form, CultureInfo, bool, Task<string?>> Run
    )
    {
        public static Case Of<T>(
            string name,
            T[] values,
            Action<TabularColumnBuilder<T>>? configure = null,
            T[]? expected = null
        ) => new(name, (form, culture, trims) => RunAsync(values, expected ?? values, configure, form, culture, trims));

        private static async Task<string?> RunAsync<T>(
            T[] values,
            T[] expected,
            Action<TabularColumnBuilder<T>>? configure,
            Form form,
            CultureInfo culture,
            bool trims
        )
        {
            TabularMap<Cell<T>> map;
            try
            {
                // WHY: The key column keeps every row from being empty; empty rows follow their own reading rule.
                map = TabularMap
                    .For<Cell<T>>()
                    .Column(x => x.Key, index: 0)
                    .Column(
                        x => x.Item,
                        index: 1,
                        configure: c =>
                        {
                            configure?.Invoke(c);
                            if (form == Form.ExcelText)
                            {
                                c.ExcelAsText();
                            }
                        })
                    .Build();
            }
            catch (TabularConfigurationException exception)
            {
                return $"Build failed: {string.Join(" | ", exception.Errors.Select(error => error.Message))}";
            }

            var rows = values
                .Select((value, index) => new Cell<T>(index, value))
                .ToArray();

            var file = new MemoryStream();
            var exported = form == Form.Csv
                ? await CsvInstance.ExportAsync(rows, map, file, new CsvExportOptions { Culture = culture }, Token)
                : await ExcelInstance.ExportAsync(rows, map, file, new ExcelExportOptions { Culture = culture }, Token);

            if (exported.Status != ReportStatus.Succeeded)
            {
                // WHY: An export that refuses a value it cannot keep is correct; it must say why.
                return exported.FatalError is null && exported.Errors.Count == 0
                    ? "export failed without an error"
                    : null;
            }

            file.Position = 0;
            var imported = form == Form.Csv
                ? await CsvInstance.ImportAsync(
                    file,
                    map,
                    new CsvImportOptions
                    {
                        Culture = culture,
                        TrimFields = trims,
                    },
                    Token)
                : await ExcelInstance.Format.ImportAsync(
                    file,
                    map,
                    new ExcelImportOptions
                    {
                        Culture = culture,
                        TrimFields = trims,
                    },
                    Token);

            if (imported.Status != ReportStatus.Succeeded)
            {
                return "export succeeded, import failed: "
                    + string.Join(" | ", imported.Errors.Select(error => $"{error.Code.Value} {error.Message}"));
            }

            var read = imported.Items!
                .Select(cell => cell.Item)
                .ToArray();

            return read.SequenceEqual(expected)
                ? null
                : $"read [{string.Join(", ", read)}], expected [{string.Join(", ", expected)}]";
        }
    }
}
