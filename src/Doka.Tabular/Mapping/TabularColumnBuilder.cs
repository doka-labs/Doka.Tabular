namespace Doka.Tabular;

/// <summary>
/// Configures one mapped column: header, direction, and cell handling, and the settings of its value. A custom
/// conversion configures the settings of its base type on its own builder.
/// </summary>
/// <typeparam name="TValue">The type of the mapped property or compose part.</typeparam>
[PublicAPI]
public sealed class TabularColumnBuilder<TValue> : TabularValueBuilder<TValue, TabularColumnBuilder<TValue>>
{
    internal TabularColumnBuilder(
        FieldSettings settings
    )
        : base(settings) { }

    /// <summary>Sets the fixed header text used for the header row and for header-name binding.</summary>
    /// <param name="text">The header text.</param>
    /// <returns>This builder.</returns>
    public TabularColumnBuilder<TValue> Header(
        string text
    )
    {
        ArgumentNullException.ThrowIfNull(text);
        Settings.Header = text;

        return this;
    }

    /// <summary>Sets the key of the header translation, resolved per call from <c>Translations</c>.</summary>
    /// <param name="key">The translation key.</param>
    /// <returns>This builder.</returns>
    public TabularColumnBuilder<TValue> HeaderTranslationKey(
        string key
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        Settings.HeaderTranslationKey = key;

        return this;
    }

    /// <summary>Releases the source value of this column for <see cref="TabularError.SourceValue" />.</summary>
    /// <returns>This builder.</returns>
    public TabularColumnBuilder<TValue> IncludeSourceValue()
    {
        Settings.IncludeSourceValue = true;

        return this;
    }

    /// <summary>Uses the field for import only; the export leaves value and header out.</summary>
    /// <returns>This builder.</returns>
    public TabularColumnBuilder<TValue> ImportOnly()
    {
        Settings.Direction = FieldDirection.ImportOnly;

        return this;
    }

    /// <summary>Uses the field for export only, for example for a computed property.</summary>
    /// <returns>This builder.</returns>
    public TabularColumnBuilder<TValue> ExportOnly()
    {
        Settings.Direction = FieldDirection.ExportOnly;

        return this;
    }

    /// <summary>Makes the header optional for header-name binding; a missing column yields the empty value.</summary>
    /// <returns>This builder.</returns>
    public TabularColumnBuilder<TValue> OptionalHeader()
    {
        Settings.IsHeaderOptional = true;

        return this;
    }

    /// <summary>Rejects Excel formula cells in this column, even with a valid stored result.</summary>
    /// <returns>This builder.</returns>
    public TabularColumnBuilder<TValue> RejectFormulas()
    {
        Settings.RejectFormulas = true;

        return this;
    }

    /// <summary>Writes the whole column as Excel text cells; without effect for CSV.</summary>
    /// <returns>This builder.</returns>
    public TabularColumnBuilder<TValue> ExcelAsText()
    {
        Settings.ExcelAsText = true;

        return this;
    }
}
