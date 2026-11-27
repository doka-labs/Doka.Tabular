using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace Doka.Tabular;

/// <summary>Reads a property path of a root object; an absent intermediate object sets <paramref name="missing" />.</summary>
internal delegate TValue PathReader<out TValue>(
    object? root,
    out bool missing
);

/// <summary>Creates a record from the member slots of a node.</summary>
internal delegate object RecordCreator(
    ValueSlot[] slots
);

/// <summary>
/// Typed accessors compiled once per shape and kept for the lifetime of the process, so a record or a field is never
/// read, written, or created through reflection per row.
/// </summary>
/// <remarks>
/// WHY: Maps are built once and used for many calls; the number of distinct shapes is bounded by the mapped types of
/// the application, so the caches do not grow with the data.
/// </remarks>
[RequiresDynamicCode("Accessors are compiled from expression trees at run time.")]
[RequiresUnreferencedCode("Accessors bind mapped properties and constructors by reflection.")]
internal static class Accessors
{
    private static readonly ConcurrentDictionary<PathKey, Delegate> s_paths = new();
    private static readonly ConcurrentDictionary<CreatorKey, RecordCreator> s_creators = new();

    /// <summary>A reader of the path, typed to the value of its last property.</summary>
    public static PathReader<TValue> Path<TValue>(
        IReadOnlyList<PropertyInfo> path
    ) => (PathReader<TValue>)s_paths.GetOrAdd(
        new PathKey(typeof(TValue), [.. path]),
        static key => CompilePath<TValue>(key));

    /// <summary>Calls a typed rule with the values of the member slots, in member order.</summary>
    public static Func<ValueSlot[], bool> Rule(
        Delegate rule,
        IReadOnlyList<MapMember> members
    )
    {
        var slots = Expression.Parameter(typeof(ValueSlot[]), "slots");
        var parameters = rule
            .Method
            .GetParameters();

        var arguments = new Expression[members.Count];
        for (var index = 0; index < members.Count; index++)
        {
            arguments[index] = Expression.Convert(
                SlotValue(slots, index, members[index].Kind.Type),
                parameters[index].ParameterType);
        }

        var call = Expression.Invoke(Expression.Constant(rule), arguments);

        return Expression
            .Lambda<Func<ValueSlot[], bool>>(call, slots)
            .Compile();
    }

    /// <summary>A creator that calls the constructor with slot values or defaults and sets the remaining members.</summary>
    public static RecordCreator Creator(
        ConstructorInfo constructor,
        IReadOnlyList<CreatorArgument> arguments,
        IReadOnlyList<(PropertyInfo Property, int Slot, Type SlotType)> setters
    ) => s_creators.GetOrAdd(
        new CreatorKey(constructor, [.. arguments], [.. setters]),
        static key => CompileCreator(key));

    private static PathReader<TValue> CompilePath<TValue>(
        PathKey key
    )
    {
        var root = Expression.Parameter(typeof(object), "root");
        var missing = Expression.Parameter(typeof(bool).MakeByRefType(), "missing");
        var result = Expression.Label(typeof(TValue), "result");
        var body = new List<Expression> { Expression.Assign(missing, Expression.Constant(false)) };
        var variables = new List<ParameterExpression>();

        // WHY: Every step reads a variable, so each property getter runs once; only the objects that own a property
        // are checked for null, as the value of the last property may itself be null.
        var current = root;
        foreach (var property in key.Path)
        {
            if (!current.Type.IsValueType
                || Nullable.GetUnderlyingType(current.Type) is not null)
            {
                body.Add(
                    Expression.IfThen(
                        Expression.Equal(current, Expression.Constant(null, current.Type)),
                        Expression.Block(
                            Expression.Assign(missing, Expression.Constant(true)),
                            Expression.Return(result, Expression.Default(typeof(TValue))))));
            }

            var next = Expression.Variable(property.PropertyType, property.Name);
            variables.Add(next);
            body.Add(
                Expression.Assign(
                    next,
                    Expression.Property(Expression.Convert(current, property.DeclaringType!), property)));

            current = next;
        }

        body.Add(Expression.Label(result, Expression.Convert(current, typeof(TValue))));

        return Expression
            .Lambda<PathReader<TValue>>(Expression.Block(variables, body), root, missing)
            .Compile();
    }

    private static RecordCreator CompileCreator(
        CreatorKey key
    )
    {
        var slots = Expression.Parameter(typeof(ValueSlot[]), "slots");
        var parameters = key.Constructor.GetParameters();
        var arguments = new Expression[key.Arguments.Length];
        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = key.Arguments[index];
            var type = parameters[index].ParameterType;
            arguments[index] = argument.Slot is { } slot
                ? Expression.Convert(SlotValue(slots, slot, argument.SlotType!), type)
                : DefaultArgument(argument.DefaultValue, type);
        }

        var created = Expression.New(key.Constructor, arguments);
        var bindings = key
            .Setters
            .Select(MemberBinding (setter) => Expression.Bind(
                setter.Property,
                Expression.Convert(SlotValue(slots, setter.Slot, setter.SlotType), setter.Property.PropertyType)))
            .ToArray();

        Expression body = bindings.Length == 0
            ? created
            : Expression.MemberInit(created, bindings);

        return Expression
            .Lambda<RecordCreator>(Expression.Convert(body, typeof(object)), slots)
            .Compile();
    }

    /// <summary>
    /// The default of an optional parameter; reflection reports enum defaults as their underlying value and the
    /// default of a value type without explicit value as null.
    /// </summary>
    private static Expression DefaultArgument(
        object? value,
        Type type
    )
    {
        if (value is null)
        {
            return Expression.Default(type);
        }

        return value.GetType() == type
            ? Expression.Constant(value, type)
            : Expression.Convert(Expression.Constant(value), type);
    }

    private static MemberExpression SlotValue(
        ParameterExpression slots,
        int index,
        Type valueType
    ) => Expression.Property(
        Expression.Convert(
            Expression.ArrayIndex(slots, Expression.Constant(index)),
            typeof(ValueSlot<>).MakeGenericType(valueType)),
        nameof(ValueSlot<>.Value));

    private sealed record PathKey(
        Type ValueType,
        PropertyInfo[] Path
    )
    {
        public bool Equals(
            PathKey? other
        ) => other is not null
            && ValueType == other.ValueType
            && Path
                .AsSpan()
                .SequenceEqual(other.Path);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(ValueType);
            foreach (var property in Path)
            {
                hash.Add(property);
            }

            return hash.ToHashCode();
        }
    }

    private sealed record CreatorKey(
        ConstructorInfo Constructor,
        CreatorArgument[] Arguments,
        (PropertyInfo Property, int Slot, Type SlotType)[] Setters
    )
    {
        public bool Equals(
            CreatorKey? other
        ) => other is not null
            && Constructor == other.Constructor
            && Arguments
                .AsSpan()
                .SequenceEqual(other.Arguments)
            && Setters
                .AsSpan()
                .SequenceEqual(other.Setters);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Constructor);
            foreach (var argument in Arguments)
            {
                hash.Add(argument);
            }

            foreach (var setter in Setters)
            {
                hash.Add(setter);
            }

            return hash.ToHashCode();
        }
    }
}

/// <summary>One constructor argument: a member slot, or the default value of an optional parameter.</summary>
internal sealed record CreatorArgument(
    int? Slot,
    Type? SlotType,
    object? DefaultValue
);
