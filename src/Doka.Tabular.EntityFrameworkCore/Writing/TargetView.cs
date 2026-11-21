namespace Doka.Tabular.EntityFrameworkCore;

/// <summary>A target definition together with the call settings of one view.</summary>
internal sealed record TargetView(
    TargetDefinition Definition,
    TabularWriteOptions Options,
    ImportLogBinding? Journal
);
