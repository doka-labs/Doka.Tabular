using System.Collections;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Doka.Tabular.EntityFrameworkCore;

/// <summary>EF state of one row: entities tracked during the row and snapshots of entities tracked before it.</summary>
internal sealed class RowTracking : IDisposable
{
    private readonly DbContext _context;

    private readonly List<(NavigationEntry Navigation, object? Value, object[]? Items)> _ownedNavigations = [];

    private readonly List<(EntityEntry Entry, PropertyValues Current, PropertyValues Original, EntityState State)>
        _protected = [];

    private readonly List<EntityEntry> _tracked = [];

    public RowTracking(
        DbContext context
    )
    {
        _context = context;
        _context.ChangeTracker.Tracked += OnTracked;
    }

    public void Dispose() => _context.ChangeTracker.Tracked -= OnTracked;

    /// <summary>Remembers the values of an entity that was tracked before the row, so a rejected row can restore them.</summary>
    public void Protect(
        EntityEntry entry
    )
    {
        if (_tracked.Any(tracked => ReferenceEquals(tracked.Entity, entry.Entity))
            || _protected.Any(item => ReferenceEquals(item.Entry.Entity, entry.Entity)))
        {
            return;
        }

        _protected.Add((entry, entry.CurrentValues.Clone(), entry.OriginalValues.Clone(), entry.State));

        // WHY: Owned entities belong to the aggregate but are tracked as entries of their own; a rejected row must
        // restore them and which owned objects the navigations point to.
        foreach (var navigation in entry.Navigations)
        {
            if (navigation is not { } owned
                || !owned.Metadata.TargetEntityType.IsOwned())
            {
                continue;
            }

            var value = owned.CurrentValue;
            var items = value is IEnumerable sequence and not string
                ? sequence
                    .Cast<object>()
                    .ToArray()
                : null;

            _ownedNavigations.Add((owned, value, items));
            foreach (var target in items
                ?? (value is null
                    ? []
                    : [value]))
            {
                Protect(_context.Entry(target));
            }
        }
    }

    /// <summary>Detaches the entities of a stored row; earlier tracked entities keep their saved state.</summary>
    public void Release()
    {
        // WHY: Lists are read by index throughout Tabular; foreach is kept for arrays, spans, and sequences.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var entryIndex = 0; entryIndex < _tracked.Count; entryIndex++)
        {
            var entry = _tracked[entryIndex];
            entry.State = EntityState.Detached;
        }

        _tracked.Clear();
        _protected.Clear();
        _ownedNavigations.Clear();
    }

    /// <summary>Resets the EF state of a rejected row so a later save does not write it.</summary>
    public void Restore()
    {
        // WHY: Lists are read by index throughout Tabular; foreach is kept for arrays, spans, and sequences.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var entryIndex = 0; entryIndex < _tracked.Count; entryIndex++)
        {
            var entry = _tracked[entryIndex];
            entry.State = EntityState.Detached;
        }

        foreach (var (navigation, value, items) in _ownedNavigations)
        {
            RestoreNavigation(navigation, value, items);
        }

        foreach (var (entry, current, original, state) in _protected)
        {
            entry.CurrentValues.SetValues(current);
            entry.OriginalValues.SetValues(original);
            entry.State = state;
        }

        _tracked.Clear();
        _protected.Clear();
        _ownedNavigations.Clear();
    }

    /// <summary>Points an owned navigation back to its objects; a collection gets back its former members.</summary>
    private static void RestoreNavigation(
        NavigationEntry navigation,
        object? value,
        object[]? items
    )
    {
        if (!ReferenceEquals(navigation.CurrentValue, value))
        {
            navigation.CurrentValue = value;
        }

        if (items is null
            || value is null
            || navigation.Metadata is not INavigation metadata)
        {
            return;
        }

        // WHY: The collection accessor of EF adds and removes members for every collection type, also sets; members
        // the row added would otherwise be found by the next change detection and saved with a later row.
        var accessor = metadata.GetCollectionAccessor()!;
        var owner = navigation.EntityEntry.Entity;
        var before = new HashSet<object>(items, ReferenceEqualityComparer.Instance);
        var now = ((IEnumerable)value)
            .Cast<object>()
            .ToArray();

        foreach (var added in now.Where(member => !before.Contains(member)))
        {
            accessor.Remove(owner, added);
        }

        var current = new HashSet<object>(now, ReferenceEqualityComparer.Instance);
        foreach (var removed in items.Where(member => !current.Contains(member)))
        {
            accessor.Add(owner, removed, forMaterialization: false);
        }
    }

    private void OnTracked(
        object? sender,
        EntityTrackedEventArgs args
    ) => _tracked.Add(args.Entry);
}
