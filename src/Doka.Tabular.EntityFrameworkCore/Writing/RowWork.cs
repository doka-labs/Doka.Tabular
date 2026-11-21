namespace Doka.Tabular.EntityFrameworkCore;

/// <summary>One validated row prepared for the targets: the records per bound target and the location for errors.</summary>
internal sealed record RowWork(
    int Index,
    IReadOnlyList<object> Records,
    TabularErrorLocation Location
);
