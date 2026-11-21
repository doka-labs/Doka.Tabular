namespace Doka.Tabular.Excel;

internal abstract class PreparedExportSheet
{
    public required ExcelSheetPlan Plan { get; init; }

    public required bool WriteHeader { get; init; }

    public abstract Task<TableExportOutcome> WriteAsync(
        ExcelWorkbookWriter workbook,
        ErrorCollector errors,
        CancellationToken cancellationToken
    );
}
