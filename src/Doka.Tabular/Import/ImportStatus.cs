namespace Doka.Tabular;

/// <summary>The facts that decide the status of an import report, for a single table or a workbook.</summary>
/// <param name="Canceled">The import was canceled without a fatal error.</param>
/// <param name="Failed">Configuration, schema, abort, or a fatal error ended the import.</param>
/// <param name="GateStopped">The import journal stopped the import before the first record.</param>
/// <param name="Skipped">The journal stopped it because the file was already imported.</param>
/// <param name="HadErrors">Data errors or rejected rows occurred.</param>
/// <param name="ErrorsFail">Data errors fail the whole import, as in a strict import with one transaction.</param>
internal readonly record struct ImportStatusFacts(
    bool Canceled,
    bool Failed,
    bool GateStopped,
    bool Skipped,
    bool HadErrors,
    bool ErrorsFail
)
{
    /// <summary>Decides the status; the order of the checks is part of the report contract.</summary>
    public ReportStatus Decide()
    {
        if (Canceled)
        {
            return ReportStatus.Canceled;
        }

        if (Failed)
        {
            return ReportStatus.Failed;
        }

        if (GateStopped)
        {
            return Skipped
                ? ReportStatus.Succeeded
                : ReportStatus.Failed;
        }

        if (!HadErrors)
        {
            return ReportStatus.Succeeded;
        }

        return ErrorsFail
            ? ReportStatus.Failed
            : ReportStatus.CompletedWithErrors;
    }
}
