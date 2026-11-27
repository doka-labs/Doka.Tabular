namespace Doka.Tabular.EntityFrameworkCore;

/// <summary>Binds roles to targets of one database description; the bind order is the write order.</summary>
[PublicAPI]
public sealed class TabularDestinationBuilder
{
    private readonly List<(BoundTarget Target, TabularSourceKey? Key)> _bound = [];
    private readonly RowMap _map;

    internal TabularDestinationBuilder(
        RowMap map
    )
    {
        _map = map;
    }

    /// <summary>Binds a role to a write target.</summary>
    /// <typeparam name="T">The role type.</typeparam>
    /// <param name="role">A role of the row map.</param>
    /// <param name="target">The write target; settings and journal belong to the destination, not to the bound target.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">The role belongs to another row map, is already bound, or the target has own settings or another database.</exception>
    public TabularDestinationBuilder Bind<T>(
        TabularRole<T> role,
        TabularWriteTarget<T> target
    )
    {
        ArgumentNullException.ThrowIfNull(target);

        return Bind(role, target.View, target.Key?.ForRole(role));
    }

    /// <summary>Binds a role to a read target.</summary>
    /// <typeparam name="T">The role type.</typeparam>
    /// <param name="role">A role of the row map.</param>
    /// <param name="target">The read target.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">The role belongs to another row map, is already bound, or the target has own settings or another database.</exception>
    public TabularDestinationBuilder Bind<T>(
        TabularRole<T> role,
        TabularLookupTarget<T> target
    )
    {
        ArgumentNullException.ThrowIfNull(target);

        return Bind(role, target.View, null);
    }

    /// <summary>Builds the destination.</summary>
    /// <returns>The destination.</returns>
    /// <exception cref="InvalidOperationException">No role is bound, or a target uses a value of a target that is not bound before it.</exception>
    public TabularRowDestination Build()
    {
        if (_bound.Count == 0)
        {
            throw new InvalidOperationException("Bind at least one role.");
        }

        for (var index = 0; index < _bound.Count; index++)
        {
            var current = _bound[index].Target;

            // WHY: Each part is checked in order and a missing source throws; the loop keeps that failure visible.
            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var part in current.Definition.Parts.Where(part => part.ValueFrom is not null))
            {
                var source = _bound
                    .Take(index)
                    .Select(entry => entry.Target)
                    .FirstOrDefault(earlier
                        => ReferenceEquals(earlier.Definition, part.ValueFrom!.Target)
                        && ReferenceEquals(earlier.Role, part.ValueFrom.Role));

                if (source is null)
                {
                    throw new InvalidOperationException(
                        $"The match of {current.Definition.EntityType.Name} uses a value of a target that is not bound before it.");
                }
            }
        }

        return new TabularRowDestination(
            _map,
            _bound
                .Select(entry => entry.Target)
                .ToArray(),
            _bound
                .Where(entry => entry.Key is not null)
                .Select(entry => entry.Key!)
                .ToArray(),
            new TabularWriteOptions(),
            null);
    }

    private TabularDestinationBuilder Bind<T>(
        TabularRole<T> role,
        TargetView view,
        TabularSourceKey? key
    )
    {
        ArgumentNullException.ThrowIfNull(role);
        if (role.Ordinal >= _map.Roles.Count
            || !ReferenceEquals(_map.Roles[role.Ordinal].Identity, role))
        {
            throw new ArgumentException("The role belongs to another row map.", nameof(role));
        }

        if (_bound.Any(entry => ReferenceEquals(entry.Target.Role, role)))
        {
            throw new ArgumentException("The role is already bound.", nameof(role));
        }

        if (view.Journal is not null
            || view.Options != new TabularWriteOptions())
        {
            throw new ArgumentException(
                "Set write options and the import log on the row destination, not on a bound target.",
                nameof(role));
        }

        if (_bound.Count > 0
            && !ReferenceEquals(_bound[0].Target.Definition.Database, view.Definition.Database))
        {
            throw new ArgumentException(
                "All targets of a row destination must come from the same database description.",
                nameof(role));
        }

        if (view.Definition.Values.Any(value
            => !ReferenceEquals(value.Role, role)
            && _map.Roles.All(other => !ReferenceEquals(other.Identity, value.Role))))
        {
            throw new ArgumentException("A value of the target refers to a role of another row map.", nameof(role));
        }

        _bound.Add((new BoundTarget(view.Definition, role, role.Ordinal), key));

        return this;
    }
}
