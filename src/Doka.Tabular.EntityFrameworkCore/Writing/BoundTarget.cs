namespace Doka.Tabular.EntityFrameworkCore;

/// <summary>A target bound to a role of a row map, or to the record itself without a role.</summary>
internal sealed record BoundTarget(
    TargetDefinition Definition,
    object? Role,
    int Ordinal
);
