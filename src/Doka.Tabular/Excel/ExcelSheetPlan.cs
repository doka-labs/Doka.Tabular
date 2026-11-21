namespace Doka.Tabular.Excel;

/// <summary>Resolved layout of one exported sheet, validated before the first write.</summary>
internal sealed class ExcelSheetPlan
{
    public required TabularSheetName Name { get; init; }

    public required ExcelLayoutOptions Layout { get; init; }

    public required IReadOnlyList<ExportColumn> Columns { get; init; }

    public required bool WriteHeader { get; init; }

    public required ExcelTableName? TableName { get; init; }

    public ExcelTableStyle TableStyle => Layout.TableStyle ?? ExcelTableStyle.Medium2;

    public bool BandedRows => Layout.BandedRows ?? true;

    public bool IsTable => Layout.Mode == ExcelLayoutMode.Table;
}
