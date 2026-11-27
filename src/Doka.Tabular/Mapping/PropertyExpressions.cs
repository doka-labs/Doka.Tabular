using System.Linq.Expressions;
using System.Reflection;

namespace Doka.Tabular;

internal static class PropertyExpressions
{
    /// <summary>Returns the single public property accessed directly on the parameter.</summary>
    public static PropertyInfo Direct<T, TValue>(
        Expression<Func<T, TValue>> expression
    )
    {
        ArgumentNullException.ThrowIfNull(expression);
        var path = Chain(expression.Body);
        if (path is not { Count: 1 })
        {
            throw new ArgumentException(
                "The expression must access one property directly, for example x => x.Email.",
                nameof(expression));
        }

        return path[0];
    }

    /// <summary>Returns the dotted property chain of the expression, for example <c>Geo.Latitude</c>.</summary>
    public static string ChainName<T, TValue>(
        Expression<Func<T, TValue>> expression
    )
    {
        ArgumentNullException.ThrowIfNull(expression);
        var path = Chain(expression.Body)
            ?? throw new ArgumentException(
                "The expression must be a property chain, for example a => a.PostalCode.",
                nameof(expression));

        return string.Join('.', path.Select(property => property.Name));
    }

    private static List<PropertyInfo>? Chain(
        Expression body
    )
    {
        var properties = new List<PropertyInfo>();
        var current = body is UnaryExpression { NodeType: ExpressionType.Convert } convert
            ? convert.Operand
            : body;

        while (current is MemberExpression { Member: PropertyInfo property } member)
        {
            properties.Insert(0, property);
            current = member.Expression;
        }

        return current is ParameterExpression && properties.Count > 0
            ? properties
            : null;
    }
}
