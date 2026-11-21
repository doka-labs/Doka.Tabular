using System.Linq.Expressions;

namespace Doka.Tabular;

/// <summary>Builds an immutable <see cref="TabularMap{T}" />. Later changes to the builder never affect a built map.</summary>
/// <typeparam name="T">The mapped type.</typeparam>
[PublicAPI]
public sealed partial class TabularMapBuilder<T>
{
    private readonly List<ExpectedException> _expectations = [];
    private readonly List<Func<MapMember>> _members = [];
    private readonly List<Func<IReadOnlyList<MapMember>, CrossRule>> _rules = [];
    private Func<TabularFields<T>, Func<TabularValues<T>, T>>? _factory;

    internal TabularMapBuilder() { }

    /// <summary>Maps a property to one file column.</summary>
    /// <typeparam name="TValue">The property type.</typeparam>
    /// <param name="property">The property, accessed directly, for example <c>x => x.Email</c>.</param>
    /// <param name="index">Zero-based declared index.</param>
    /// <param name="key">Key of the column; the default is the property name.</param>
    /// <param name="configure">Configures header, conversion, empty value, direction, and validation.</param>
    /// <returns>This builder.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public TabularMapBuilder<T> Column<TValue>(
        Expression<Func<T, TValue>> property,
        int index,
        ColumnKey? key = null,
        Action<TabularColumnBuilder<TValue>>? configure = null
    )
    {
        var info = PropertyExpressions.Direct(property);
        var settings = new FieldSettings();
        configure?.Invoke(new TabularColumnBuilder<TValue>(settings));
        var frozen = settings.Clone();
        var effectiveKey = key ?? ColumnKey.Create(info.Name);
        _members.Add(() => new ColumnMember(info, ValueKind<TValue>.Instance, index, effectiveKey, frozen.Clone()));

        return this;
    }

    /// <summary>Includes a built map for a property of the mapped type.</summary>
    /// <typeparam name="TNested">The type of the included map.</typeparam>
    /// <param name="property">The property, accessed directly.</param>
    /// <param name="map">The included map; it stays unchanged.</param>
    /// <param name="configure">Configures index offset, key and header prefixes of this inclusion.</param>
    /// <returns>This builder.</returns>
    public TabularMapBuilder<T> Nested<TNested>(
        Expression<Func<T, TNested?>> property,
        TabularMap<TNested> map,
        Action<TabularEmbeddingBuilder<TNested>>? configure = null
    )
    {
        ArgumentNullException.ThrowIfNull(map);
        var info = PropertyExpressions.Direct(property);
        var embedding = new EmbeddingSettings();
        configure?.Invoke(new TabularEmbeddingBuilder<TNested>(embedding));
        var frozen = embedding.Clone();
        var isNullable = Nullability.IsNullable(info);
        _members.Add(() => new NestedMember(info, ValueKind<TNested?>.Instance, map.Model, frozen.Clone(), isNullable));

        return this;
    }

    /// <summary>
    /// Declares an exception type from constructor, factory, or compose <c>read</c> as an expected rejection of the
    /// record, reported as a data error with <paramref name="code" />.
    /// </summary>
    /// <typeparam name="TException">The exception type, including derived types.</typeparam>
    /// <param name="code">The error code.</param>
    /// <returns>This builder.</returns>
    public TabularMapBuilder<T> Expect<TException>(
        TabularErrorCode code
    )
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(code);
        _expectations.Add(new ExpectedException(typeof(TException), code));

        return this;
    }

    /// <summary>
    /// Creates records with a factory instead of a constructor; it takes precedence over automatic constructor
    /// binding and is the way to choose among several constructors. <paramref name="factory" /> runs once in
    /// <see cref="Build" />: it prepares the fields it reads and returns the function that creates each record.
    /// </summary>
    /// <example>
    /// <code>
    /// .UseFactory(fields =>
    /// {
    ///     var id = fields.Of(x => x.Id);
    ///     var email = fields.Of(x => x.Email);
    ///     return values => new Contact(values.Get(id), values.Get(email));
    /// })
    /// </code>
    /// </example>
    /// <param name="factory">Prepares the fields and returns the function that creates a record from their values.</param>
    /// <returns>This builder.</returns>
    public TabularMapBuilder<T> UseFactory(
        Func<TabularFields<T>, Func<TabularValues<T>, T>> factory
    )
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;

        return this;
    }

    /// <summary>Validates the structure and builds the immutable map.</summary>
    /// <returns>The map.</returns>
    /// <exception cref="TabularConfigurationException">The structure is invalid.</exception>
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public TabularMap<T> Build()
    {
        var members = _members
            .Select(create => create())
            .ToList();

        var rules = new List<CrossRule>();
        var errors = new List<TabularError>();

        // WHY: Lists are read by index throughout Tabular; foreach is kept for arrays, spans, and sequences.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var createRuleIndex = 0; createRuleIndex < _rules.Count; createRuleIndex++)
        {
            var createRule = _rules[createRuleIndex];
            try
            {
                rules.Add(createRule(members));
            }
            catch (InvalidOperationException exception)
            {
                errors.Add(TabularError.Configuration(exception.Message));
            }
        }

        var (modelFactory, factoryReads) = PrepareFactory(members, errors);
        var model = new MapModel(typeof(T), members, modelFactory, factoryReads, [.. _expectations], rules);
        errors.AddRange(MapStructureValidator.Validate(model));
        if (errors.Count > 0)
        {
            throw new TabularConfigurationException(errors);
        }

        return new TabularMap<T>(model);
    }

    /// <summary>Runs the preparing part of the factory once and keeps the members it reads.</summary>
    private (Func<RecordValues, object>? Factory, IReadOnlyList<int> Reads) PrepareFactory(
        IReadOnlyList<MapMember> members,
        List<TabularError> errors
    )
    {
        if (_factory is not { } prepare)
        {
            return (null, []);
        }

        var fields = new TabularFields<T>(members);
        Func<TabularValues<T>, T> create;
        try
        {
            create = prepare(fields);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // WHY: The preparing part of the factory runs once at Build; its failure is a configuration error of the
            // map, without the text of the exception.
            fields.Complete();
            errors.AddRange(fields.Errors);
            errors.Add(TabularError.Configuration("The preparing part of the factory threw an exception."));

            return (null, []);
        }

        var reads = fields.Complete();
        errors.AddRange(fields.Errors);

        // WHY: The factory is application code; a null result becomes a configuration error instead of a later crash.
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (create is null)
        {
            errors.Add(TabularError.Configuration("The factory returned no function that creates the records."));

            return (null, []);
        }

        return (values => create(values.Typed<T>())!, reads);
    }
}
