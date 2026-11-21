using System.Runtime.CompilerServices;

namespace Doka.Tabular.Samples.MinimalApi;

/// <summary>An exported contact; <see cref="FullName" /> exists only for the export.</summary>
public sealed record Contact(
    int Number,
    string Email,
    string FirstName,
    string LastName,
    DateOnly Since
)
{
    public string FullName => $"{FirstName} {LastName}";
}

/// <summary>Body of the export endpoints: format, visible columns, and language of the headers.</summary>
public sealed record ContactExportRequest(
    TabularType Type,
    IReadOnlyList<string> Columns,
    string Language
);

/// <summary>Response shape for reports; codes are stable and translated by the client.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public sealed record ReportResponse(
    Guid OperationId,
    string Status,
    long Rows,
    long TotalErrorCount,
    IReadOnlyList<ErrorResponse> Errors
);

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public sealed record ErrorResponse(
    string Code,
    long? Row,
    int? Column,
    string? Property
);

/// <summary>Maps, header texts, and the application's in-memory contact list.</summary>
public static class ContactCatalog
{
    public static readonly TabularMap<Contact> Map = TabularMap
        .For<Contact>()
        .Column(x => x.Number,
            index: 0,
            key: ColumnKey.Create("number"),
            configure: c => c
                .Header("Nummer")
                .HeaderTranslationKey("number")
                .Validate(number => number > 0))
        .Column(x => x.Email,
            index: 1,
            key: ColumnKey.Create("email"),
            configure: c => c
                .Header("E-Mail")
                .HeaderTranslationKey("email")
                .Required())
        .Column(x => x.FirstName,
            index: 2,
            key: ColumnKey.Create("firstName"),
            configure: c => c
                .Header("Vorname")
                .HeaderTranslationKey("firstName"))
        .Column(x => x.LastName,
            index: 3,
            key: ColumnKey.Create("lastName"),
            configure: c => c
                .Header("Nachname")
                .HeaderTranslationKey("lastName"))
        .Column(x => x.Since,
            index: 4,
            key: ColumnKey.Create("since"),
            configure: c => c
                .Header("Seit")
                .HeaderTranslationKey("since")
                .ExcelNumberFormat("dd.mm.yyyy"))
        .Column(x => x.FullName,
            index: 5,
            key: ColumnKey.Create("fullName"),
            configure: c => c
                .Header("Name")
                .HeaderTranslationKey("fullName")
                .ExportOnly())
        .Build();

    public static IReadOnlyDictionary<string, string> Headers(
        string language
    ) => language switch
    {
        "en" => new Dictionary<string, string>
        {
            ["number"] = "Number",
            ["email"] = "Email",
            ["firstName"] = "First name",
            ["lastName"] = "Last name",
            ["since"] = "Since",
            ["fullName"] = "Name",
        },
        _ => new Dictionary<string, string>
        {
            ["number"] = "Nummer",
            ["email"] = "E-Mail",
            ["firstName"] = "Vorname",
            ["lastName"] = "Nachname",
            ["since"] = "Seit",
            ["fullName"] = "Name",
        },
    };

    public static ReportResponse Response<T>(
        ImportReport<T> report
    ) => new(report.Diagnostics.OperationId,
        report.Status.ToString(),
        report.RowsImported,
        report.TotalErrorCount,
        Errors(report.Errors));

    public static ReportResponse Response(
        ExportReport report
    ) => new(report.Diagnostics.OperationId,
        report.Status.ToString(),
        report.RowsWritten,
        report.TotalErrorCount,
        Errors(report.Errors));

    private static List<ErrorResponse> Errors(
        IEnumerable<TabularError> errors
    ) =>
    [
        .. errors.Select(error => new ErrorResponse(error.Code.Value,
            error.Location.RowNumber,
            error.Location.ColumnIndex,
            error.Location.PropertyPath?.Value)),
    ];
}

/// <summary>The application's contact store; an asynchronous sequence is enumerated once without loading everything first.</summary>
public sealed class ContactStore
{
    private readonly List<Contact> _contacts =
    [
        new(1, "ada@example.org", "Ada", "Lovelace", new DateOnly(2024, 4, 1)),
        new(2, "grace@example.org", "Grace", "Hopper", new DateOnly(2024, 5, 15)),
    ];

    public async IAsyncEnumerable<Contact> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        foreach (var contact in _contacts.ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();

            yield return contact;
        }
    }

    public void AddRange(
        IEnumerable<Contact> contacts
    ) => _contacts.AddRange(contacts);
}
