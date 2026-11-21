using System.Reflection;

namespace Doka.Tabular;

internal static class Nullability
{
    private static readonly NullabilityInfoContext s_context = new();

    public static bool IsNullable(
        PropertyInfo property
    )
    {
        if (property.PropertyType.IsValueType)
        {
            return Nullable.GetUnderlyingType(property.PropertyType) is not null;
        }

        // WHY: NullabilityInfoContext is not thread-safe; maps may be built concurrently.
        lock (s_context)
        {
            return s_context.Create(property)
                    .ReadState
                != NullabilityState.NotNull;
        }
    }
}
