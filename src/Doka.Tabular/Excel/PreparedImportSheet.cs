namespace Doka.Tabular.Excel;

/// <summary>One sheet of a multi-sheet import after the call-time checks.</summary>
internal abstract class PreparedImportSheet
{
    public required TabularSheetName Name { get; init; }

    public required ExcelReadSettings ReadSettings { get; init; }

    public required ConversionContext Context { get; init; }

    public abstract IImportSinkHandle? Sink { get; }

    /// <summary>Gets the run of the last read, also when the read ended with a technical abort.</summary>
    public SheetRun? LastRun { get; protected set; }

    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public abstract Task<SheetRun> ReadAsync(
        ExcelPackage package,
        ExcelSheetInfo sheet,
        ErrorCollector errors,
        CancellationToken cancellationToken
    );

    public abstract object EmptyReport(
        ReportStatus status,
        TabularReportDiagnostics diagnostics
    );
}
