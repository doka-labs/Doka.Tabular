using System.Linq.Expressions;

namespace Doka.Tabular;

public sealed partial class TabularMapBuilder<T>
{
    /// <summary>
    /// Maps a property to 2 file columns. <paramref name="read" /> runs only when every part is converted and valid;
    /// <paramref name="write" /> always returns every part value.
    /// </summary>
    /// <typeparam name="TValue">The property type.</typeparam>
    /// <typeparam name="T1">Type of part 1.</typeparam>
    /// <typeparam name="T2">Type of part 2.</typeparam>
    /// <param name="property">The property, accessed directly.</param>
    /// <param name="part1">Declares part 1.</param>
    /// <param name="part2">Declares part 2.</param>
    /// <param name="read">Combines the part values.</param>
    /// <param name="write">Splits the value into part values.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">A part declaration returns no field.</exception>
    public TabularMapBuilder<T> Compose<TValue, T1, T2>(
        Expression<Func<T, TValue>> property,
        Func<TabularComposeFields, TabularComposeField<T1>> part1,
        Func<TabularComposeFields, TabularComposeField<T2>> part2,
        Func<T1, T2, TValue> read,
        Func<TValue, (T1, T2)> write
    )
    {
        var info = PropertyExpressions.Direct(property);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(write);
        var fields = new TabularComposeFields();
        ComposePart[] declared =
        [
            Declared(part1, fields, nameof(part1)),
            Declared(part2, fields, nameof(part2)),
        ];

        _members.Add(() => new ComposeMember(info, declared, new ComposeBinding<TValue, T1, T2>(read, write)));

        return this;
    }

    /// <summary>
    /// Maps a property to 3 file columns. <paramref name="read" /> runs only when every part is converted and valid;
    /// <paramref name="write" /> always returns every part value.
    /// </summary>
    /// <typeparam name="TValue">The property type.</typeparam>
    /// <typeparam name="T1">Type of part 1.</typeparam>
    /// <typeparam name="T2">Type of part 2.</typeparam>
    /// <typeparam name="T3">Type of part 3.</typeparam>
    /// <param name="property">The property, accessed directly.</param>
    /// <param name="part1">Declares part 1.</param>
    /// <param name="part2">Declares part 2.</param>
    /// <param name="part3">Declares part 3.</param>
    /// <param name="read">Combines the part values.</param>
    /// <param name="write">Splits the value into part values.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">A part declaration returns no field.</exception>
    public TabularMapBuilder<T> Compose<TValue, T1, T2, T3>(
        Expression<Func<T, TValue>> property,
        Func<TabularComposeFields, TabularComposeField<T1>> part1,
        Func<TabularComposeFields, TabularComposeField<T2>> part2,
        Func<TabularComposeFields, TabularComposeField<T3>> part3,
        Func<T1, T2, T3, TValue> read,
        Func<TValue, (T1, T2, T3)> write
    )
    {
        var info = PropertyExpressions.Direct(property);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(write);
        var fields = new TabularComposeFields();
        ComposePart[] declared =
        [
            Declared(part1, fields, nameof(part1)),
            Declared(part2, fields, nameof(part2)),
            Declared(part3, fields, nameof(part3)),
        ];

        _members.Add(() => new ComposeMember(info, declared, new ComposeBinding<TValue, T1, T2, T3>(read, write)));

        return this;
    }

    /// <summary>
    /// Maps a property to 4 file columns. <paramref name="read" /> runs only when every part is converted and valid;
    /// <paramref name="write" /> always returns every part value.
    /// </summary>
    /// <typeparam name="TValue">The property type.</typeparam>
    /// <typeparam name="T1">Type of part 1.</typeparam>
    /// <typeparam name="T2">Type of part 2.</typeparam>
    /// <typeparam name="T3">Type of part 3.</typeparam>
    /// <typeparam name="T4">Type of part 4.</typeparam>
    /// <param name="property">The property, accessed directly.</param>
    /// <param name="part1">Declares part 1.</param>
    /// <param name="part2">Declares part 2.</param>
    /// <param name="part3">Declares part 3.</param>
    /// <param name="part4">Declares part 4.</param>
    /// <param name="read">Combines the part values.</param>
    /// <param name="write">Splits the value into part values.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">A part declaration returns no field.</exception>
    public TabularMapBuilder<T> Compose<TValue, T1, T2, T3, T4>(
        Expression<Func<T, TValue>> property,
        Func<TabularComposeFields, TabularComposeField<T1>> part1,
        Func<TabularComposeFields, TabularComposeField<T2>> part2,
        Func<TabularComposeFields, TabularComposeField<T3>> part3,
        Func<TabularComposeFields, TabularComposeField<T4>> part4,
        Func<T1, T2, T3, T4, TValue> read,
        Func<TValue, (T1, T2, T3, T4)> write
    )
    {
        var info = PropertyExpressions.Direct(property);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(write);
        var fields = new TabularComposeFields();
        ComposePart[] declared =
        [
            Declared(part1, fields, nameof(part1)),
            Declared(part2, fields, nameof(part2)),
            Declared(part3, fields, nameof(part3)),
            Declared(part4, fields, nameof(part4)),
        ];

        _members.Add(() => new ComposeMember(info, declared, new ComposeBinding<TValue, T1, T2, T3, T4>(read, write)));

        return this;
    }

    /// <summary>The part a declaration returns; checked here, so a missing declaration names its argument.</summary>
    private static ComposePart Declared<TPart>(
        Func<TabularComposeFields, TabularComposeField<TPart>> part,
        TabularComposeFields fields,
        string name
    )
    {
        ArgumentNullException.ThrowIfNull(part, name);

        // WHY: The declaration is application code; a null result would otherwise surface as a NullReferenceException.
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        return (part(fields) ?? throw new ArgumentException("The part declaration returned no field.", name)).Part;
    }
}
