using System.Runtime.CompilerServices;

namespace Doka.Tabular.EntityFrameworkCore;

/// <summary>The key holders and the predicate of one match; the holders take the key values of each row.</summary>
internal sealed class MatchQuery
{
    private readonly IReadOnlyList<MatchPart> _parts;

    public MatchQuery(
        IReadOnlyList<MatchPart> parts,
        IStrongBox[] holders,
        LambdaExpression predicate
    )
    {
        _parts = parts;
        Holders = holders;
        Predicate = predicate;
    }

    public IStrongBox[] Holders { get; }

    public LambdaExpression Predicate { get; }

    /// <summary>Whether an entity matches the staged key values under the comparers of the pairs.</summary>
    public bool Matches(
        object entity
    )
    {
        // WHY: Runs per row and pairs each part with its holder by index; a query would capture the entity.
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < _parts.Count; index++)
        {
            if (!_parts[index]
                .Matches(entity, Holders[index]))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// The match queries of one write session. Target definitions are shared by calls, so the mutable key holders belong
/// to the session that writes the rows.
/// </summary>
internal sealed class MatchQueries
{
    private readonly Dictionary<TargetDefinition, MatchQuery> _queries = [];

    public MatchQuery For(
        TargetDefinition definition
    )
    {
        if (!_queries.TryGetValue(definition, out var query))
        {
            query = definition.CreateQuery();
            _queries[definition] = query;
        }

        return query;
    }
}
