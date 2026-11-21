namespace Doka.Tabular.Excel;

internal sealed record ExcelReadSettings(
    int StartRow,
    int? EndRow,
    bool VisibleRowsOnly,
    bool EmitHeaderRow
);
