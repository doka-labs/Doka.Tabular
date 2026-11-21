namespace Doka.Tabular;

/// <summary>
/// One error of a build, import, or export. Messages are fixed English texts and never contain source values,
/// exception texts, or raw rows.
/// </summary>
[PublicAPI]
public sealed class TabularError
{
    private TabularError(
        TabularErrorCode code,
        TabularErrorKind kind,
        string message,
        TabularErrorLocation location,
        IReadOnlyList<TabularErrorLocation> relatedLocations,
        string? sourceValue
    )
    {
        Code = code;
        Kind = kind;
        Message = message;
        Location = location;

        // WHY: A report is a snapshot; a cast back to a list must not change the locations of an error.
        RelatedLocations = relatedLocations.Count == 0
            ? []
            : Array.AsReadOnly(relatedLocations.ToArray());

        SourceValue = sourceValue;
    }

    /// <summary>Gets the stable code; see <see cref="TabularErrorCodes" />.</summary>
    public TabularErrorCode Code { get; }

    /// <summary>Gets the kind of error.</summary>
    public TabularErrorKind Kind { get; }

    /// <summary>Gets a fixed English description for diagnostics; applications use <see cref="Code" />.</summary>
    public string Message { get; }

    /// <summary>Gets the location of the error.</summary>
    public TabularErrorLocation Location { get; }

    /// <summary>Gets related locations, for example other occurrences of a duplicate key.</summary>
    public IReadOnlyList<TabularErrorLocation> RelatedLocations { get; }

    /// <summary>
    /// Gets the source value when the map releases it for this column (<c>IncludeSourceValue()</c>); otherwise
    /// <see langword="null" />.
    /// </summary>
    public string? SourceValue { get; }

    internal static TabularError Create(
        TabularErrorCode code,
        TabularErrorKind kind,
        string message,
        TabularErrorLocation? location = null,
        IReadOnlyList<TabularErrorLocation>? relatedLocations = null,
        string? sourceValue = null
    ) => new(code, kind, message, location ?? TabularErrorLocation.None, relatedLocations ?? [], sourceValue);

    internal static TabularError Configuration(
        string message,
        TabularErrorLocation? location = null
    ) => Create(TabularErrorCodes.Configuration.Invalid, TabularErrorKind.Configuration, message, location);

    internal TabularError WithLocation(
        TabularErrorLocation location
    ) => new(Code, Kind, Message, location, RelatedLocations, SourceValue);
}
