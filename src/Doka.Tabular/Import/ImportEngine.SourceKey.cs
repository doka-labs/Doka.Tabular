namespace Doka.Tabular;

internal static partial class ImportEngine
{
    /// <summary>
    /// Adds the rows with incomplete or repeated keys among <paramref name="candidates" /> to
    /// <paramref name="rejected" />; <see langword="false" /> when the call was canceled.
    /// </summary>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    private static bool ApplySourceKey<TItem>(
        IReadOnlyList<ImportedRow<TItem>> candidates,
        TabularSourceKey key,
        ImportPlan<TItem> plan,
        ErrorCollector errors,
        HashSet<ImportedRow<TItem>> rejected,
        CancellationToken cancellationToken
    )
    {
        var columns = key
            .Parts
            .Select(part => part.CreateColumn(key.ItemType))
            .ToArray();

        if (KeyedRows(
                candidates,
                key,
                plan,
                columns,
                errors,
                rejected,
                cancellationToken) is not { } keyed)
        {
            return false;
        }

        var byKey = new Dictionary<int, List<ImportedRow<TItem>>>(new KeyComparer(columns));
        var groups = new List<List<ImportedRow<TItem>>>();
        for (var index = 0; index < keyed.Count; index++)
        {
            // WHY: Comparing keys is the long part of the check; a canceled call stops here.
            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            if (!byKey.TryGetValue(index, out var members))
            {
                members = [];
                byKey.Add(index, members);
                groups.Add(members);
            }

            members.Add(keyed[index]);
        }

        // WHY: Lists are read by index throughout Tabular; foreach is kept for arrays, spans, and sequences.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            // WHY: Also a comparison of the last key or the building of the details can see the cancellation.
            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            if (groups[groupIndex] is { Count: > 1 } duplicates
                && !RejectDuplicates(duplicates, key, errors, rejected, cancellationToken))
            {
                return false;
            }
        }

        return !cancellationToken.IsCancellationRequested;
    }

    /// <summary>
    /// The valid rows with complete keys, in key column order; a row with an empty key part is rejected.
    /// <see langword="null" /> when the call was canceled.
    /// </summary>
    private static List<ImportedRow<TItem>>? KeyedRows<TItem>(
        IReadOnlyList<ImportedRow<TItem>> candidates,
        TabularSourceKey key,
        ImportPlan<TItem> plan,
        KeyColumn[] columns,
        ErrorCollector errors,
        HashSet<ImportedRow<TItem>> rejected,
        CancellationToken cancellationToken
    )
    {
        var roleIndex = -1;
        for (var index = 0; index < plan.Shape.Roles.Count; index++)
        {
            var role = plan.Shape.Roles[index];
            if (role.Model.Type == key.ItemType
                && (key.Role is null || role.Identity == key.Role))
            {
                roleIndex = index;
                break;
            }
        }

        var keyed = new List<ImportedRow<TItem>>();

        // WHY: Lists are read by index throughout Tabular; foreach is kept for arrays, spans, and sequences.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var rowIndex = 0; rowIndex < candidates.Count; rowIndex++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            var row = candidates[rowIndex];
            var dto = row.Item is TabularRow group
                ? group.Items[roleIndex]
                : row.Item!;

            var emptyPart = -1;
            for (var part = 0; part < columns.Length && emptyPart < 0; part++)
            {
                if (!columns[part]
                    .Stage(dto))
                {
                    emptyPart = part;
                }
            }

            if (emptyPart < 0)
            {
                foreach (var column in columns)
                {
                    column.Commit();
                }

                keyed.Add(row);
                continue;
            }

            errors.Add(
                TabularError.Create(
                    TabularErrorCodes.Key.Incomplete,
                    TabularErrorKind.Data,
                    "A part of the source key is empty.",
                    RowLocation(row, key.Parts[emptyPart].Path)));

            rejected.Add(row);
        }

        return keyed;
    }

    /// <summary>
    /// Rejects every row of a group of equal keys; each error names the other rows of the group.
    /// <see langword="false" /> when the call was canceled.
    /// </summary>
    private static bool RejectDuplicates<TItem>(
        List<ImportedRow<TItem>> members,
        TabularSourceKey key,
        ErrorCollector errors,
        HashSet<ImportedRow<TItem>> rejected,
        CancellationToken cancellationToken
    )
    {
        // WHY: Lists are read by index throughout Tabular; foreach is kept for arrays, spans, and sequences.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var rowIndex = 0; rowIndex < members.Count; rowIndex++)
        {
            // WHY: A group of n equal keys builds n * (n - 1) related locations; a canceled call stops per row.
            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            var row = members[rowIndex];

            // WHY: A group of n equal keys names n - 1 other rows per error; they are built only for a kept detail.
            List<TabularErrorLocation> related = [];
            if (errors.KeepsNextDetail)
            {
                // WHY: Lists are read by index throughout Tabular; foreach is kept for arrays, spans, and sequences.
                // ReSharper disable once ForCanBeConvertedToForeach
                for (var otherIndex = 0; otherIndex < members.Count; otherIndex++)
                {
                    if (otherIndex != rowIndex)
                    {
                        related.Add(RowLocation(members[otherIndex], null));
                    }
                }
            }

            errors.Add(
                TabularError.Create(
                    TabularErrorCodes.Key.Duplicate,
                    TabularErrorKind.Data,
                    "The source key occurs in several records.",
                    RowLocation(row, key.Parts[0].Path),
                    related));

            rejected.Add(row);
        }

        return true;
    }

    private static TabularErrorLocation RowLocation<TItem>(
        ImportedRow<TItem> row,
        TabularPropertyPath? path
    ) => new()
    {
        RowNumber = row.RowNumber,
        RecordNumber = row.RecordNumber,
        SheetName = row.SheetName,
        PropertyPath = path,
    };

    /// <summary>Compares rows by the typed values of all key columns.</summary>
    private sealed class KeyComparer : IEqualityComparer<int>
    {
        private readonly KeyColumn[] _columns;

        public KeyComparer(
            KeyColumn[] columns
        )
        {
            _columns = columns;
        }

        public bool Equals(
            int x,
            int y
        )
        {
            // WHY: Runs per key comparison; a query would capture both row positions in a closure.
            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var column in _columns)
            {
                if (!column.Equal(x, y))
                {
                    return false;
                }
            }

            return true;
        }

        public int GetHashCode(
            int obj
        )
        {
            var hash = new HashCode();
            foreach (var column in _columns)
            {
                hash.Add(column.Hash(obj));
            }

            return hash.ToHashCode();
        }
    }
}
