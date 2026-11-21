namespace Doka.Tabular;

/// <summary>Custom conversions over built-in base types.</summary>
[PublicAPI]
public static class TabularConversionColumnExtensions
{
    /// <summary>
    /// Binds a custom type through a built-in base type: the cell is read into <typeparamref name="TBase" /> with all
    /// rules of that type, then <paramref name="read" /> maps it; on export <paramref name="write" /> maps back.
    /// </summary>
    /// <typeparam name="TBase">The built-in base type.</typeparam>
    /// <typeparam name="TCustom">The mapped custom type.</typeparam>
    /// <param name="builder">The field builder.</param>
    /// <param name="read">Maps the base value to the custom value.</param>
    /// <param name="write">Maps the custom value to the base value.</param>
    /// <param name="configure">Configures the base-type settings, for example padding or formats.</param>
    /// <returns>The field builder.</returns>
    public static TabularColumnBuilder<TCustom> Convert<TBase, TCustom>(
        this TabularColumnBuilder<TCustom> builder,
        Func<TBase, TCustom> read,
        Func<TCustom, TBase> write,
        Action<TabularBaseTypeBuilder<TBase>>? configure = null
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(write);

        var baseSettings = new FieldSettings();
        configure?.Invoke(new TabularBaseTypeBuilder<TBase>(baseSettings));
        builder.Settings.Conversion = new CustomConversion<TBase, TCustom>(read, write, baseSettings);

        return builder;
    }
}
