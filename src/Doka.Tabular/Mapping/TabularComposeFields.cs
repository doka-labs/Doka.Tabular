namespace Doka.Tabular;

/// <summary>Declares the file columns of a composed value.</summary>
[PublicAPI]
public sealed class TabularComposeFields
{
    internal TabularComposeFields() { }

    /// <summary>Declares one part column.</summary>
    /// <typeparam name="TPart">The part type; a built-in type.</typeparam>
    /// <param name="index">Zero-based declared index.</param>
    /// <param name="key">Key of the part column.</param>
    /// <param name="configure">Configures header, conversion, empty value, and validation of the part.</param>
    /// <returns>The part.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]

    // WHY: The method is the fluent entry point on the lambda parameter of Compose; as a static member it would
    // not be reachable through that parameter, so it stays an instance member without instance state.
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Fluent builder entry point.")]

    // ReSharper disable once MemberCanBeMadeStatic.Global
    public TabularComposeField<TPart> Field<TPart>(
        int index,
        ColumnKey key,
        Action<TabularColumnBuilder<TPart>>? configure = null
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        var settings = new FieldSettings();
        configure?.Invoke(new TabularColumnBuilder<TPart>(settings));

        return new TabularComposeField<TPart>(new ComposePart(index, key, ValueKind<TPart>.Instance, settings.Clone()));
    }
}

/// <summary>One part column of a composed value.</summary>
/// <typeparam name="TPart">The part type.</typeparam>
[PublicAPI]
public sealed class TabularComposeField<TPart>
{
    internal TabularComposeField(
        ComposePart part
    )
    {
        Part = part;
    }

    internal ComposePart Part { get; }
}
