namespace Doka.Tabular.EntityFrameworkCore;

/// <summary>Outcome of one target for one row before saving.</summary>
internal sealed record TargetStep(
    object? Entity,
    bool Skipped,
    TabularError? RowError,
    TabularError? Fatal
);
