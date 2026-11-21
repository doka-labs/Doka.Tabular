namespace Doka.Tabular.Excel;

/// <summary>Counts expanded bytes of all parts of one call against <c>MaxExpandedBytes</c>, while reading.</summary>
internal sealed class ExpansionBudget
{
    private readonly long? _limit;
    private long _used;

    public ExpansionBudget(
        long? limit
    )
    {
        _limit = limit;
    }

    public void Add(
        int bytes
    )
    {
        _used += bytes;
        if (_limit is { } limit
            && _used > limit)
        {
            throw TabularResourceException.Limit("expanded bytes", limit);
        }
    }
}
