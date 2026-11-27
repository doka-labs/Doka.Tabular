using System.Text.Json.Serialization;

namespace Doka.Tabular.Samples.NestedMaps;

public enum OrderState
{
    [JsonStringEnumMemberName("open")]
    Open = 1,

    [JsonStringEnumMemberName("shipped")]
    Shipped = 2,
}

[Flags]
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public enum Services
{
    None = 0,
    Gift = 1,
    Express = 2,
    Insurance = 4,
}

public sealed record Money(
    decimal Amount,
    string Currency
);

public sealed record Address(
    string City,
    string PostalCode
);

public sealed record Order(
    string Number,
    Address Billing,
    Address? Shipping,
    Money Total,
    DateTimeOffset PlacedAt,
    OrderState State,
    Services Services,
    bool Paid
);

/// <summary>
/// One address map included twice with own positions, keys, and header prefixes; composed money and points in time;
/// enum, flags, and bool texts; and translations per call, exported and reimported in CSV and Excel.
/// </summary>
public static class NestedMapsSample
{
    private static readonly TabularMap<Address> s_address = TabularMap
        .For<Address>()
        .Column(
            x => x.City,
            index: 0,
            key: ColumnKey.Create("city"),
            configure: c => c
                .Header("Ort")
                .HeaderTranslationKey("city")
                .Required())
        .Column(
            x => x.PostalCode,
            index: 1,
            key: ColumnKey.Create("postalCode"),
            configure: c => c
                .Header("PLZ")
                .HeaderTranslationKey("postalCode")
                .PadLeft(totalWidth: 5, paddingChar: '0'))
        .Build();

    private static readonly TabularMap<Order> s_map = TabularMap
        .For<Order>()
        .Column(
            x => x.Number,
            index: 0,
            key: ColumnKey.Create("number"),
            configure: c => c
                .Header("Auftrag")
                .HeaderTranslationKey("number"))
        .Nested(
            x => x.Billing,
            s_address,
            e => e
                .StartIndex(1)
                .KeyPrefix(ColumnKey.Create("billing"))
                .HeaderPrefix("Rechnung ")
                .HeaderPrefixTranslationKey("billingPrefix"))
        .Nested(
            x => x.Shipping,
            s_address,
            e => e
                .StartIndex(3)
                .KeyPrefix(ColumnKey.Create("shipping"))
                .HeaderPrefix("Lieferung ")
                .HeaderPrefixTranslationKey("shippingPrefix"))
        .Compose(
            x => x.Total,
            c => c.Field<long>(
                index: 5,
                key: ColumnKey.Create("totalMinor"),
                configure: f => f
                    .Header("Betrag (Cent)")
                    .HeaderTranslationKey("totalMinor")),
            c => c.Field<string>(
                index: 6,
                key: ColumnKey.Create("currency"),
                configure: f => f
                    .Header("Währung")
                    .HeaderTranslationKey("currency")),
            read: (minor, currency) => new Money(minor / 100m, currency),
            write: money => ((long)(money.Amount * 100), money.Currency))
        .Compose(
            x => x.PlacedAt,
            c => c.Field<DateTime>(
                index: 7,
                key: ColumnKey.Create("placedUtc"),
                configure: f => f
                    .Header("Bestellt (UTC)")
                    .HeaderTranslationKey("placedUtc")
                    .ExcelAsUtc()),
            c => c.Field<int>(
                index: 8,
                key: ColumnKey.Create("offsetMinutes"),
                configure: f => f
                    .Header("Versatz (Minuten)")
                    .HeaderTranslationKey("offsetMinutes")),
            read: (utc, minutes)
                => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToOffset(
                    TimeSpan.FromMinutes(minutes)),
            write: placed => (placed.UtcDateTime, (int)placed.Offset.TotalMinutes))
        .Column(
            x => x.State,
            index: 9,
            key: ColumnKey.Create("state"),
            configure: c => c
                .Header("Status")
                .HeaderTranslationKey("state")
                .TranslationKeys(state => $"state.{state}"))
        .Column(
            x => x.Services,
            index: 10,
            key: ColumnKey.Create("services"),
            configure: c => c
                .Header("Leistungen")
                .HeaderTranslationKey("services")
                .FlagsSeparator('|'))
        .Column(
            x => x.Paid,
            index: 11,
            key: ColumnKey.Create("paid"),
            configure: c => c
                .Header("Bezahlt")
                .HeaderTranslationKey("paid")
                .TrueText("ja")
                .FalseText("nein"))
        .Build();

    private static readonly IReadOnlyDictionary<string, string> s_english = new Dictionary<string, string>
    {
        ["number"] = "Order",
        ["city"] = "City",
        ["postalCode"] = "Postal code",
        ["billingPrefix"] = "Billing ",
        ["shippingPrefix"] = "Shipping ",
        ["totalMinor"] = "Total (cents)",
        ["currency"] = "Currency",
        ["placedUtc"] = "Placed (UTC)",
        ["offsetMinutes"] = "Offset (minutes)",
        ["state"] = "State",
        ["services"] = "Services",
        ["paid"] = "Paid",
        ["state.Open"] = "Open",
        ["state.Shipped"] = "Shipped",
    };

    private static IReadOnlyList<Order> Orders { get; } =
    [
        new(
            "A-1",
            new Address("Dresden", "01067"),
            null,
            new Money(12.34m, "EUR"),
            new DateTimeOffset(
                2024,
                4,
                1,
                14,
                30,
                0,
                TimeSpan.FromHours(2)),
            OrderState.Open,
            Services.Gift | Services.Express,
            false),
        new(
            "A-2",
            new Address("Bonn", "53111"),
            new Address("Köln", "50667"),
            new Money(99m, "USD"),
            new DateTimeOffset(
                2024,
                4,
                1,
                8,
                0,
                0,
                TimeSpan.FromHours(-5)),
            OrderState.Shipped,
            Services.None,
            true),
    ];

    public static async Task RunAsync(
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        var export = new TabularExport(
            new CsvFormat(new CsvFormatOptions()),
            new ExcelFormat(new ExcelFormatOptions()));

        var import = new TabularImport(
            new CsvFormat(new CsvFormatOptions()),
            new ExcelFormat(new ExcelFormatOptions()));

        foreach (var type in new[] { TabularType.Csv, TabularType.Excel })
        {
            foreach (var translations in new[] { null, s_english })
            {
                await RoundTripAsync(export, import, type, translations, output, cancellationToken);
            }
        }

        await ExportSelectionAsync(export, output, cancellationToken);
    }

    /// <summary>Exports with headers and imports the file again by header name, in the same language.</summary>
    private static async Task RoundTripAsync(
        TabularExport export,
        TabularImport import,
        TabularType type,
        IReadOnlyDictionary<string, string>? translations,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        var (report, file) = await export.ExportAsync(
            Orders,
            s_map,
            type,
            new TabularExportOptions
            {
                WriteHeader = true,
                Translations = translations,
            },
            cancellationToken);

        await using (file)
        {
            if (type == TabularType.Csv
                && file is not null)
            {
                using var reader = new StreamReader(file, leaveOpen: true);
                await output.WriteLineAsync(await reader.ReadLineAsync(cancellationToken));
                file.Position = 0;
            }

            // Header-name import finds the columns through the same headers the export wrote, in the same language.
            var reimport = await import.ImportAsync(
                file!,
                type,
                s_map,
                new TabularImportOptions
                {
                    Binding = ColumnBinding.ByHeaderName,
                    Translations = translations,
                },
                cancellationToken);

            var same = reimport.Items!.SequenceEqual(Orders);
            await output.WriteLineAsync(
                $"{type} {(translations is null ? "fixed texts" : "English")}: export {report.Status}, reimport {reimport.Status}, equal {same}");
        }
    }

    private static async Task ExportSelectionAsync(
        TabularExport export,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        var (selected, selection) = await export.ExportAsync(
            Orders,
            s_map,
            TabularType.Csv,
            new TabularExportOptions
            {
                ColumnKeys =
                [
                    ColumnKey.Create("number"), ColumnKey.Create("shipping.postalCode"),
                    ColumnKey.Create("currency"),
                ],
                WriteHeader = true,
            },
            cancellationToken);

        await using (selection)
        {
            using var reader = new StreamReader(selection!);
            await output.WriteLineAsync($"Selection {selected.Status}:");
            await output.WriteLineAsync(await reader.ReadToEndAsync(cancellationToken));
        }
    }
}
