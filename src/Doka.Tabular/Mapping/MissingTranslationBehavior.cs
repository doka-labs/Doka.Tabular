namespace Doka.Tabular;

/// <summary>Behavior when <c>Translations</c> are given but a needed key is missing.</summary>
[PublicAPI]
public enum MissingTranslationBehavior
{
    /// <summary>The call fails with <c>tabular.translation.missing</c>.</summary>
    Error,

    /// <summary>The fixed text is used; without a fixed text it remains an error.</summary>
    UseFixedText,
}
