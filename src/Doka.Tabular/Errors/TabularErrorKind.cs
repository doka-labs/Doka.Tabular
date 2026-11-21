namespace Doka.Tabular;

/// <summary>Kind of a <see cref="TabularError" />.</summary>
[PublicAPI]
public enum TabularErrorKind
{
    /// <summary>The map, the call options, or the schema of the file do not allow processing.</summary>
    Configuration,

    /// <summary>A source value, a record, or a written value violates a rule.</summary>
    Data,

    /// <summary>A technical failure ended the operation.</summary>
    Technical,
}
