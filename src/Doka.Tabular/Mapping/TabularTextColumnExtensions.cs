namespace Doka.Tabular;

/// <summary>Settings for text fields.</summary>
[PublicAPI]
public static class TabularTextColumnExtensions
{
    /// <summary>Pads the text on the left to <paramref name="totalWidth" /> characters after trimming and before validation, and before writing. Longer values stay unchanged.</summary>
    /// <typeparam name="TText"><see cref="string" /> or nullable <see cref="string" />.</typeparam>
    /// <typeparam name="TBuilder">The builder of the column or of the base type of a custom conversion.</typeparam>
    /// <param name="builder">The field builder.</param>
    /// <param name="totalWidth">Total width after padding.</param>
    /// <param name="paddingChar">Padding character.</param>
    /// <returns>The field builder.</returns>
    /// <exception cref="ArgumentException">The field is not a text field.</exception>
    public static TBuilder PadLeft<TText, TBuilder>(
        this TabularValueBuilder<TText, TBuilder> builder,
        int totalWidth,
        char paddingChar
    )
        where TText : IEquatable<string?>?
        where TBuilder : TabularValueBuilder<TText, TBuilder>
    {
        // WHY: Builders of string and string? are the same runtime type but differ in nullability, so one
        // non-generic overload cannot serve both; the constraint admits text, the check rejects other types.
        if (typeof(TText) != typeof(string))
        {
            throw new ArgumentException("PadLeft applies to text fields.", nameof(builder));
        }

        ArgumentNullException.ThrowIfNull(builder);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(totalWidth);
        builder.Settings.Padding = new FieldPadding(totalWidth, paddingChar);

        return builder.This;
    }

    /// <summary>Reads an empty text as <see langword="null" /> instead of the empty string; for nullable text fields.</summary>
    /// <typeparam name="TBuilder">The builder of the column or of the base type of a custom conversion.</typeparam>
    /// <param name="builder">The field builder.</param>
    /// <returns>The field builder.</returns>
    public static TBuilder EmptyStringAsNull<TBuilder>(
        this TabularValueBuilder<string?, TBuilder> builder
    )
        where TBuilder : TabularValueBuilder<string?, TBuilder>
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Settings.EmptyStringAsNull = true;

        return builder.This;
    }
}
