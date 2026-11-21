namespace Doka.Tabular;

/// <summary>
/// Configures the base type of a custom conversion: its conversion and rules. Settings of the column, such as header
/// or direction, belong to the builder of the column.
/// </summary>
/// <typeparam name="TBase">The built-in base type.</typeparam>
[PublicAPI]
public sealed class TabularBaseTypeBuilder<TBase> : TabularValueBuilder<TBase, TabularBaseTypeBuilder<TBase>>
{
    internal TabularBaseTypeBuilder(
        FieldSettings settings
    )
        : base(settings) { }
}
