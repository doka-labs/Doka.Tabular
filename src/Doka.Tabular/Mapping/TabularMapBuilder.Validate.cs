using System.Linq.Expressions;
using System.Reflection;

namespace Doka.Tabular;

public sealed partial class TabularMapBuilder<T>
{
    /// <summary>
    /// Adds a synchronous rule over 2 mapped properties. On import it runs before the record is created and only
    /// when all its fields are valid; on export only with <paramref name="alsoOnExport" /> and only when all its
    /// fields are selected.
    /// </summary>
    /// <typeparam name="T1">Type of property 1.</typeparam>
    /// <typeparam name="T2">Type of property 2.</typeparam>
    /// <param name="field1">Mapped property 1.</param>
    /// <param name="field2">Mapped property 2.</param>
    /// <param name="rule">Returns <see langword="false" /> for an invalid combination.</param>
    /// <param name="code">Error code of a violation; <see langword="null" /> uses the general validation code.</param>
    /// <param name="alsoOnExport">Whether the rule also runs on export.</param>
    /// <returns>This builder.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public TabularMapBuilder<T> Validate<T1, T2>(
        Expression<Func<T, T1>> field1,
        Expression<Func<T, T2>> field2,
        Func<T1, T2, bool> rule,
        TabularErrorCode? code = null,
        bool alsoOnExport = false
    )
    {
        ArgumentNullException.ThrowIfNull(rule);
        PropertyInfo[] properties = [PropertyExpressions.Direct(field1), PropertyExpressions.Direct(field2)];
        AddRule(properties, rule, code, alsoOnExport);

        return this;
    }

    /// <summary>
    /// Adds a synchronous rule over 3 mapped properties. On import it runs before the record is created and only
    /// when all its fields are valid; on export only with <paramref name="alsoOnExport" /> and only when all its
    /// fields are selected.
    /// </summary>
    /// <typeparam name="T1">Type of property 1.</typeparam>
    /// <typeparam name="T2">Type of property 2.</typeparam>
    /// <typeparam name="T3">Type of property 3.</typeparam>
    /// <param name="field1">Mapped property 1.</param>
    /// <param name="field2">Mapped property 2.</param>
    /// <param name="field3">Mapped property 3.</param>
    /// <param name="rule">Returns <see langword="false" /> for an invalid combination.</param>
    /// <param name="code">Error code of a violation; <see langword="null" /> uses the general validation code.</param>
    /// <param name="alsoOnExport">Whether the rule also runs on export.</param>
    /// <returns>This builder.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public TabularMapBuilder<T> Validate<T1, T2, T3>(
        Expression<Func<T, T1>> field1,
        Expression<Func<T, T2>> field2,
        Expression<Func<T, T3>> field3,
        Func<T1, T2, T3, bool> rule,
        TabularErrorCode? code = null,
        bool alsoOnExport = false
    )
    {
        ArgumentNullException.ThrowIfNull(rule);
        PropertyInfo[] properties =
        [
            PropertyExpressions.Direct(field1), PropertyExpressions.Direct(field2), PropertyExpressions.Direct(field3),
        ];

        AddRule(properties, rule, code, alsoOnExport);

        return this;
    }

    /// <summary>
    /// Adds a synchronous rule over 4 mapped properties. On import it runs before the record is created and only
    /// when all its fields are valid; on export only with <paramref name="alsoOnExport" /> and only when all its
    /// fields are selected.
    /// </summary>
    /// <typeparam name="T1">Type of property 1.</typeparam>
    /// <typeparam name="T2">Type of property 2.</typeparam>
    /// <typeparam name="T3">Type of property 3.</typeparam>
    /// <typeparam name="T4">Type of property 4.</typeparam>
    /// <param name="field1">Mapped property 1.</param>
    /// <param name="field2">Mapped property 2.</param>
    /// <param name="field3">Mapped property 3.</param>
    /// <param name="field4">Mapped property 4.</param>
    /// <param name="rule">Returns <see langword="false" /> for an invalid combination.</param>
    /// <param name="code">Error code of a violation; <see langword="null" /> uses the general validation code.</param>
    /// <param name="alsoOnExport">Whether the rule also runs on export.</param>
    /// <returns>This builder.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public TabularMapBuilder<T> Validate<T1, T2, T3, T4>(
        Expression<Func<T, T1>> field1,
        Expression<Func<T, T2>> field2,
        Expression<Func<T, T3>> field3,
        Expression<Func<T, T4>> field4,
        Func<T1, T2, T3, T4, bool> rule,
        TabularErrorCode? code = null,
        bool alsoOnExport = false
    )
    {
        ArgumentNullException.ThrowIfNull(rule);
        PropertyInfo[] properties =
        [
            PropertyExpressions.Direct(field1), PropertyExpressions.Direct(field2), PropertyExpressions.Direct(field3),
            PropertyExpressions.Direct(field4),
        ];

        AddRule(properties, rule, code, alsoOnExport);

        return this;
    }

    /// <summary>
    /// Adds a synchronous rule over 5 mapped properties. On import it runs before the record is created and only
    /// when all its fields are valid; on export only with <paramref name="alsoOnExport" /> and only when all its
    /// fields are selected.
    /// </summary>
    /// <typeparam name="T1">Type of property 1.</typeparam>
    /// <typeparam name="T2">Type of property 2.</typeparam>
    /// <typeparam name="T3">Type of property 3.</typeparam>
    /// <typeparam name="T4">Type of property 4.</typeparam>
    /// <typeparam name="T5">Type of property 5.</typeparam>
    /// <param name="field1">Mapped property 1.</param>
    /// <param name="field2">Mapped property 2.</param>
    /// <param name="field3">Mapped property 3.</param>
    /// <param name="field4">Mapped property 4.</param>
    /// <param name="field5">Mapped property 5.</param>
    /// <param name="rule">Returns <see langword="false" /> for an invalid combination.</param>
    /// <param name="code">Error code of a violation; <see langword="null" /> uses the general validation code.</param>
    /// <param name="alsoOnExport">Whether the rule also runs on export.</param>
    /// <returns>This builder.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public TabularMapBuilder<T> Validate<T1, T2, T3, T4, T5>(
        Expression<Func<T, T1>> field1,
        Expression<Func<T, T2>> field2,
        Expression<Func<T, T3>> field3,
        Expression<Func<T, T4>> field4,
        Expression<Func<T, T5>> field5,
        Func<T1, T2, T3, T4, T5, bool> rule,
        TabularErrorCode? code = null,
        bool alsoOnExport = false
    )
    {
        ArgumentNullException.ThrowIfNull(rule);
        PropertyInfo[] properties =
        [
            PropertyExpressions.Direct(field1), PropertyExpressions.Direct(field2), PropertyExpressions.Direct(field3),
            PropertyExpressions.Direct(field4), PropertyExpressions.Direct(field5),
        ];

        AddRule(properties, rule, code, alsoOnExport);

        return this;
    }

    /// <summary>
    /// Adds a synchronous rule over 6 mapped properties. On import it runs before the record is created and only
    /// when all its fields are valid; on export only with <paramref name="alsoOnExport" /> and only when all its
    /// fields are selected.
    /// </summary>
    /// <typeparam name="T1">Type of property 1.</typeparam>
    /// <typeparam name="T2">Type of property 2.</typeparam>
    /// <typeparam name="T3">Type of property 3.</typeparam>
    /// <typeparam name="T4">Type of property 4.</typeparam>
    /// <typeparam name="T5">Type of property 5.</typeparam>
    /// <typeparam name="T6">Type of property 6.</typeparam>
    /// <param name="field1">Mapped property 1.</param>
    /// <param name="field2">Mapped property 2.</param>
    /// <param name="field3">Mapped property 3.</param>
    /// <param name="field4">Mapped property 4.</param>
    /// <param name="field5">Mapped property 5.</param>
    /// <param name="field6">Mapped property 6.</param>
    /// <param name="rule">Returns <see langword="false" /> for an invalid combination.</param>
    /// <param name="code">Error code of a violation; <see langword="null" /> uses the general validation code.</param>
    /// <param name="alsoOnExport">Whether the rule also runs on export.</param>
    /// <returns>This builder.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public TabularMapBuilder<T> Validate<T1, T2, T3, T4, T5, T6>(
        Expression<Func<T, T1>> field1,
        Expression<Func<T, T2>> field2,
        Expression<Func<T, T3>> field3,
        Expression<Func<T, T4>> field4,
        Expression<Func<T, T5>> field5,
        Expression<Func<T, T6>> field6,
        Func<T1, T2, T3, T4, T5, T6, bool> rule,
        TabularErrorCode? code = null,
        bool alsoOnExport = false
    )
    {
        ArgumentNullException.ThrowIfNull(rule);
        PropertyInfo[] properties =
        [
            PropertyExpressions.Direct(field1), PropertyExpressions.Direct(field2), PropertyExpressions.Direct(field3),
            PropertyExpressions.Direct(field4), PropertyExpressions.Direct(field5), PropertyExpressions.Direct(field6),
        ];

        AddRule(properties, rule, code, alsoOnExport);

        return this;
    }

    /// <summary>
    /// Adds a synchronous rule over 7 mapped properties. On import it runs before the record is created and only
    /// when all its fields are valid; on export only with <paramref name="alsoOnExport" /> and only when all its
    /// fields are selected.
    /// </summary>
    /// <typeparam name="T1">Type of property 1.</typeparam>
    /// <typeparam name="T2">Type of property 2.</typeparam>
    /// <typeparam name="T3">Type of property 3.</typeparam>
    /// <typeparam name="T4">Type of property 4.</typeparam>
    /// <typeparam name="T5">Type of property 5.</typeparam>
    /// <typeparam name="T6">Type of property 6.</typeparam>
    /// <typeparam name="T7">Type of property 7.</typeparam>
    /// <param name="field1">Mapped property 1.</param>
    /// <param name="field2">Mapped property 2.</param>
    /// <param name="field3">Mapped property 3.</param>
    /// <param name="field4">Mapped property 4.</param>
    /// <param name="field5">Mapped property 5.</param>
    /// <param name="field6">Mapped property 6.</param>
    /// <param name="field7">Mapped property 7.</param>
    /// <param name="rule">Returns <see langword="false" /> for an invalid combination.</param>
    /// <param name="code">Error code of a violation; <see langword="null" /> uses the general validation code.</param>
    /// <param name="alsoOnExport">Whether the rule also runs on export.</param>
    /// <returns>This builder.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public TabularMapBuilder<T> Validate<T1, T2, T3, T4, T5, T6, T7>(
        Expression<Func<T, T1>> field1,
        Expression<Func<T, T2>> field2,
        Expression<Func<T, T3>> field3,
        Expression<Func<T, T4>> field4,
        Expression<Func<T, T5>> field5,
        Expression<Func<T, T6>> field6,
        Expression<Func<T, T7>> field7,
        Func<T1, T2, T3, T4, T5, T6, T7, bool> rule,
        TabularErrorCode? code = null,
        bool alsoOnExport = false
    )
    {
        ArgumentNullException.ThrowIfNull(rule);
        PropertyInfo[] properties =
        [
            PropertyExpressions.Direct(field1), PropertyExpressions.Direct(field2), PropertyExpressions.Direct(field3),
            PropertyExpressions.Direct(field4), PropertyExpressions.Direct(field5), PropertyExpressions.Direct(field6),
            PropertyExpressions.Direct(field7),
        ];

        AddRule(properties, rule, code, alsoOnExport);

        return this;
    }

    /// <summary>
    /// Adds a synchronous rule over 8 mapped properties. On import it runs before the record is created and only
    /// when all its fields are valid; on export only with <paramref name="alsoOnExport" /> and only when all its
    /// fields are selected.
    /// </summary>
    /// <typeparam name="T1">Type of property 1.</typeparam>
    /// <typeparam name="T2">Type of property 2.</typeparam>
    /// <typeparam name="T3">Type of property 3.</typeparam>
    /// <typeparam name="T4">Type of property 4.</typeparam>
    /// <typeparam name="T5">Type of property 5.</typeparam>
    /// <typeparam name="T6">Type of property 6.</typeparam>
    /// <typeparam name="T7">Type of property 7.</typeparam>
    /// <typeparam name="T8">Type of property 8.</typeparam>
    /// <param name="field1">Mapped property 1.</param>
    /// <param name="field2">Mapped property 2.</param>
    /// <param name="field3">Mapped property 3.</param>
    /// <param name="field4">Mapped property 4.</param>
    /// <param name="field5">Mapped property 5.</param>
    /// <param name="field6">Mapped property 6.</param>
    /// <param name="field7">Mapped property 7.</param>
    /// <param name="field8">Mapped property 8.</param>
    /// <param name="rule">Returns <see langword="false" /> for an invalid combination.</param>
    /// <param name="code">Error code of a violation; <see langword="null" /> uses the general validation code.</param>
    /// <param name="alsoOnExport">Whether the rule also runs on export.</param>
    /// <returns>This builder.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public TabularMapBuilder<T> Validate<T1, T2, T3, T4, T5, T6, T7, T8>(
        Expression<Func<T, T1>> field1,
        Expression<Func<T, T2>> field2,
        Expression<Func<T, T3>> field3,
        Expression<Func<T, T4>> field4,
        Expression<Func<T, T5>> field5,
        Expression<Func<T, T6>> field6,
        Expression<Func<T, T7>> field7,
        Expression<Func<T, T8>> field8,
        Func<T1, T2, T3, T4, T5, T6, T7, T8, bool> rule,
        TabularErrorCode? code = null,
        bool alsoOnExport = false
    )
    {
        ArgumentNullException.ThrowIfNull(rule);
        PropertyInfo[] properties =
        [
            PropertyExpressions.Direct(field1), PropertyExpressions.Direct(field2), PropertyExpressions.Direct(field3),
            PropertyExpressions.Direct(field4), PropertyExpressions.Direct(field5), PropertyExpressions.Direct(field6),
            PropertyExpressions.Direct(field7), PropertyExpressions.Direct(field8),
        ];

        AddRule(properties, rule, code, alsoOnExport);

        return this;
    }

    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    private void AddRule(
        PropertyInfo[] properties,
        Delegate rule,
        TabularErrorCode? code,
        bool alsoOnExport
    ) => _rules.Add(members =>
    {
        var involved = properties
            .Select(property => members.FirstOrDefault(member => member.Name == property.Name)
                ?? throw new InvalidOperationException(
                    $"The rule refers to the property {property.Name}, which is not mapped."))
            .ToArray();

        return new CrossRule(involved, Accessors.Rule(rule, involved), code, alsoOnExport);
    });
}
