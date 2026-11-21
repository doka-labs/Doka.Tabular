namespace Doka.Tabular;

/// <summary>Collects the errors of one call with the detail limit.</summary>
internal sealed class ErrorCollector
{
    private readonly List<TabularError> _details = [];
    private readonly int? _limit;

    public ErrorCollector(
        int? limit
    )
    {
        // WHY: A negative limit is reported as a configuration error; it must not hide that error itself.
        _limit = limit is < 0
            ? null
            : limit;
    }

    public long TotalCount { get; private set; }

    public long OmittedCount => TotalCount - _details.Count;

    public TabularError? Fatal { get; private set; }

    /// <summary>Gets whether a technical error was added, also one that did not end the run or is not kept as detail.</summary>
    public bool HasTechnical { get; private set; }

    /// <summary>Gets whether the next added error is kept as a detail, so that expensive details are built only then.</summary>
    public bool KeepsNextDetail => _limit is null || _details.Count < _limit;

    /// <summary>The configuration error of a negative detail limit; every entry point of a call checks it.</summary>
    public static TabularError? LimitProblem(
        int? limit
    ) => limit < 0
        ? TabularError.Configuration("MaxErrorDetails must not be negative.")
        : null;

    public void Add(
        TabularError error
    )
    {
        TotalCount++;
        HasTechnical |= error.Kind == TabularErrorKind.Technical;

        if (_limit is null
            || _details.Count < _limit)
        {
            _details.Add(error);
        }
    }

    /// <summary>Records the error that ends the run; it counts once and is always visible as fatal error.</summary>
    public void AddFatal(
        TabularError error
    )
    {
        Add(error);
        Fatal ??= error;
    }

    public IReadOnlyList<TabularError> Snapshot() => [.. _details];
}
