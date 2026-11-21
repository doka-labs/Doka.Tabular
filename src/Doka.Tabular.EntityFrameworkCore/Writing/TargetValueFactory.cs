namespace Doka.Tabular.EntityFrameworkCore;

internal static class TargetValueFactory
{
    public static TabularTargetValue<TValue> Create<TDto, TEntity, TValue>(
        TargetDefinition definition,
        TabularRole<TDto> role,
        Expression<Func<TEntity, TValue>> value
    )
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(value);
        var read = value.Compile();
        var handle = new TabularTargetValue<TValue>(definition, role, entity => read((TEntity)entity));
        definition.AddValue(handle);

        return handle;
    }
}
