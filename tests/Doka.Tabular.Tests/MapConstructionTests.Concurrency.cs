using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Tests;

/// <summary>
/// One map serves concurrent calls: exports with other selections and translations and a header-name import with
/// another column order give what each gives alone, also with nested and composed members.
/// </summary>
public sealed partial class MapConstructionTests
{
    private static readonly TabularMap<Invoice> s_invoices = InvoiceMap(_ => true);

    private static readonly Dictionary<string, string> s_german = new()
    {
        ["email"] = "E-Mail",
        ["city"] = "Ort",
        ["billing"] = "Rechnung ",
        ["minor"] = "Cent",
        ["currency"] = "Waehrung",
    };

    private static readonly Dictionary<string, string> s_english = new()
    {
        ["email"] = "Email",
        ["city"] = "City",
        ["billing"] = "Billing ",
        ["minor"] = "Minor",
        ["currency"] = "Currency",
    };

    /// <summary>The invoice map with nested and composed members; the rule sees every email the call reads or writes.</summary>
    private static TabularMap<Invoice> InvoiceMap(
        Func<string, bool> emailRule
    ) => TabularMap
        .For<Invoice>()
        .Column(
            x => x.Email,
            index: 0,
            key: ColumnKey.Create("email"),
            configure: c => c
                .HeaderTranslationKey("email")
                .Validate(emailRule, alsoOnExport: true))
        .Nested(
            x => x.Billing,
            TabularMap
                .For<Place>()
                .Column(
                    x => x.City,
                    index: 0,
                    key: ColumnKey.Create("city"),
                    configure: c => c.HeaderTranslationKey("city"))
                .Build(),
            e => e
                .StartIndex(1)
                .KeyPrefix(ColumnKey.Create("billing"))
                .HeaderPrefixTranslationKey("billing"))
        .Compose(
            x => x.Amount,
            c => c.Field<long>(
                index: 2,
                key: ColumnKey.Create("minor"),
                configure: f => f.HeaderTranslationKey("minor")),
            c => c.Field<string>(
                index: 3,
                key: ColumnKey.Create("currency"),
                configure: f => f.HeaderTranslationKey("currency")),
            read: (minor, currency) => new Price(minor, currency),
            write: price => (price.Minor, price.Currency))
        .Build();

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task ConcurrentCallsWithOneNestedAndComposedMap_GiveWhatEachGivesAlone(
        TabularType type
    )
    {
        // Arrange
        Invoice[] rows =
        [
            .. Enumerable
                .Range(1, 200)
                .Select(number
                    => new Invoice($"u{number}@example.org", new Place($"City {number}"), new Price(number, "EUR"))),
        ];

        var german = new TabularExportOptions
        {
            WriteHeader = true,
            Translations = s_german,
            ColumnKeys =
            [
                ColumnKey.Create("currency"), ColumnKey.Create("email"), ColumnKey.Create("billing.city"),
                ColumnKey.Create("minor"),
            ],
        };

        var english = new TabularExportOptions
        {
            WriteHeader = true,
            Translations = s_english,
        };

        var byName = new TabularImportOptions
        {
            Binding = ColumnBinding.ByHeaderName,
            Translations = s_german,
        };

        var reordered = (await Export.ExportAsync(rows, s_invoices, type, german, Token)).File!;

        // WHY: Every call meets the others at the same record inside the rule of the shared map, so the three calls
        // use the map at the same time. The meeting has no deadline, and a call that fails before it leaves it.
        using var meeting = new Meeting(3);
        var shared = InvoiceMap(email =>
        {
            if (email == "u100@example.org")
            {
                // ReSharper disable once AccessToDisposedClosure
                // WHY: The rule runs during the awaited calls, before the captured meeting is disposed.
                meeting.Arrive(Token);
            }

            return true;
        });

        // Act
        // WHY: The equality with single runs is the checked contract, so the single and the concurrent runs form one
        // action.
        var germanAlone = Cells(type, (await Export.ExportAsync(rows, s_invoices, type, german, Token)).File!);
        var englishAlone = Cells(type, (await Export.ExportAsync(rows, s_invoices, type, english, Token)).File!);
        reordered.Position = 0;
        var importAlone = (await Import.ImportAsync(reordered, type, s_invoices, byName, Token)).Items;
        var germanTask = meeting.JoinAsync(() => Export.ExportAsync(rows, shared, type, german, Token), Token);
        var englishTask = meeting.JoinAsync(() => Export.ExportAsync(rows, shared, type, english, Token), Token);
        reordered.Position = 0;
        var importTask = meeting.JoinAsync(() => Import.ImportAsync(reordered, type, shared, byName, Token), Token);
        await Task.WhenAll(germanTask, englishTask, importTask);

        // Assert
        Assert.Equal(3, meeting.Met);
        Assert.Equal(germanAlone, Cells(type, (await germanTask).File!));
        Assert.Equal(englishAlone, Cells(type, (await englishTask).File!));
        Assert.Equal(importAlone, (await importTask).Items);
        Assert.Equal(rows, (await importTask).Items!);
        Assert.Equal(["Waehrung", "E-Mail", "Rechnung Ort", "Cent"], germanAlone[0]);
        Assert.Equal(["Email", "Billing City", "Minor", "Currency"], englishAlone[0]);
    }

    public sealed record Invoice(
        string Email,
        Place Billing,
        Price Amount
    );

    public sealed record Place(
        string City
    );

    public sealed record Price(
        long Minor,
        string Currency
    );
}
