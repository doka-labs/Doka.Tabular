// WHY: The core and the Excel test applications link this file; each uses only some of its members.
// ReSharper disable UnusedMember.Global

namespace Doka.Tabular.Tests.Infrastructure;

/// <summary>
/// The Excel format of the tests: every workbook an export writes successfully is checked independently with the
/// Open XML SDK before the test sees it.
/// </summary>
internal sealed class CheckedExcelFormat
{
    private readonly ExcelFormat _format;

    public CheckedExcelFormat(
        ExcelFormat format
    )
    {
        _format = format;
    }

    /// <summary>Gets the format itself, for APIs that take an <see cref="ExcelFormat" />.</summary>
    public ExcelFormat Format => _format;

    public async Task<ExportReport> ExportAsync(
        ExcelWorkbookExport sheets,
        Stream target,
        CancellationToken cancellationToken
    ) => ExportChecks.Checked(
        await _format
            .ExportAsync(sheets, target, cancellationToken)
            .ConfigureAwait(false),
        target,
        true);

    public async Task<ExportReport> ExportAsync(
        ExcelWorkbookExport sheets,
        Stream target,
        ExcelWorkbookExportOptions options,
        CancellationToken cancellationToken
    ) => ExportChecks.Checked(
        await _format
            .ExportAsync(sheets, target, options, cancellationToken)
            .ConfigureAwait(false),
        target,
        true);

    public async Task<BufferedExportResult> ExportAsync(
        ExcelWorkbookExport sheets,
        CancellationToken cancellationToken
    ) => ExportChecks.Checked(
        await _format
            .ExportAsync(sheets, cancellationToken)
            .ConfigureAwait(false),
        true);

    public async Task<ExportReport> ExportAsync(
        ExcelWorkbookExport sheets,
        string path,
        CancellationToken cancellationToken
    ) => ExportChecks.CheckedPath(
        await _format
            .ExportAsync(sheets, path, cancellationToken)
            .ConfigureAwait(false),
        path,
        true);

    public async Task<ExportReport> ExportAsync<T>(
        IAsyncEnumerable<T> rows,
        TabularMap<T> map,
        Stream target,
        CancellationToken cancellationToken
    ) => ExportChecks.Checked(
        await _format
            .ExportAsync(rows, map, target, cancellationToken)
            .ConfigureAwait(false),
        target,
        true);

    public async Task<BufferedExportResult> ExportAsync<T>(
        IEnumerable<T> rows,
        TabularMap<T> map,
        ExcelExportOptions options,
        CancellationToken cancellationToken
    ) => ExportChecks.Checked(
        await _format
            .ExportAsync(rows, map, options, cancellationToken)
            .ConfigureAwait(false),
        true);

    public async Task<ExportReport> ExportAsync<T>(
        IEnumerable<T> rows,
        TabularMap<T> map,
        Stream target,
        ExcelExportOptions options,
        CancellationToken cancellationToken
    ) => ExportChecks.Checked(
        await _format
            .ExportAsync(rows, map, target, options, cancellationToken)
            .ConfigureAwait(false),
        target,
        true);

    public async Task<ExportReport> ExportAsync<T>(
        IEnumerable<T> rows,
        TabularMap<T> map,
        Stream target,
        CancellationToken cancellationToken
    ) => ExportChecks.Checked(
        await _format
            .ExportAsync(rows, map, target, cancellationToken)
            .ConfigureAwait(false),
        target,
        true);

    public async Task<BufferedExportResult> ExportAsync<T>(
        IEnumerable<T> rows,
        TabularMap<T> map,
        CancellationToken cancellationToken
    ) => ExportChecks.Checked(
        await _format
            .ExportAsync(rows, map, cancellationToken)
            .ConfigureAwait(false),
        true);

    public async Task<ExportReport> ExportAsync<T>(
        IEnumerable<T> rows,
        TabularMap<T> map,
        string path,
        ExcelExportOptions options,
        CancellationToken cancellationToken
    ) => ExportChecks.CheckedPath(
        await _format
            .ExportAsync(rows, map, path, options, cancellationToken)
            .ConfigureAwait(false),
        path,
        true);

    public async Task<ExportReport> ExportAsync<T>(
        IEnumerable<T> rows,
        TabularMap<T> map,
        string path,
        CancellationToken cancellationToken
    ) => ExportChecks.CheckedPath(
        await _format
            .ExportAsync(rows, map, path, cancellationToken)
            .ConfigureAwait(false),
        path,
        true);

    public Task<ExcelWorkbookImportReport> ImportAsync(
        Stream source,
        ExcelWorkbookImport sheets,
        CancellationToken cancellationToken
    ) => _format.ImportAsync(source, sheets, cancellationToken);

    public Task<ImportReport<T>> ImportAsync<T>(
        Stream source,
        TabularMap<T> map,
        ExcelImportOptions options,
        CancellationToken cancellationToken
    ) => _format.ImportAsync(source, map, options, cancellationToken);

    public Task<ImportReport<T>> ImportAsync<T>(
        Stream source,
        TabularMap<T> map,
        CancellationToken cancellationToken
    ) => _format.ImportAsync(source, map, cancellationToken);
}

/// <summary>The common export of the tests; every Excel workbook it writes successfully is checked independently.</summary>
internal sealed class CheckedTabularExport
{
    private readonly TabularExport _export;

    public CheckedTabularExport(
        TabularExport export
    )
    {
        _export = export;
    }

    public async Task<BufferedExportResult> ExportAsync(
        IEnumerable<TabularRow> rows,
        RowMap map,
        TabularType type,
        CancellationToken cancellationToken
    ) => ExportChecks.Checked(
        await _export
            .ExportAsync(rows, map, type, cancellationToken)
            .ConfigureAwait(false),
        type == TabularType.Excel);

    public async Task<BufferedExportResult> ExportAsync<T>(
        IEnumerable<T> rows,
        RowExportMap<T> map,
        TabularType type,
        TabularExportOptions options,
        CancellationToken cancellationToken
    ) => ExportChecks.Checked(
        await _export
            .ExportAsync(rows, map, type, options, cancellationToken)
            .ConfigureAwait(false),
        type == TabularType.Excel);

    public async Task<BufferedExportResult> ExportAsync<T>(
        IEnumerable<T> rows,
        TabularMap<T> map,
        TabularType type,
        TabularExportOptions options,
        CancellationToken cancellationToken
    ) => ExportChecks.Checked(
        await _export
            .ExportAsync(rows, map, type, options, cancellationToken)
            .ConfigureAwait(false),
        type == TabularType.Excel);

    public async Task<BufferedExportResult> ExportAsync<T>(
        IEnumerable<T> rows,
        TabularMap<T> map,
        TabularType type,
        CancellationToken cancellationToken
    ) => ExportChecks.Checked(
        await _export
            .ExportAsync(rows, map, type, cancellationToken)
            .ConfigureAwait(false),
        type == TabularType.Excel);

    public async Task<ExportReport> ExportAsync<T>(
        IEnumerable<T> rows,
        TabularMap<T> map,
        Stream target,
        TabularType type,
        TabularExportOptions options,
        CancellationToken cancellationToken
    ) => ExportChecks.Checked(
        await _export
            .ExportAsync(rows, map, target, type, options, cancellationToken)
            .ConfigureAwait(false),
        target,
        type == TabularType.Excel);
}

/// <summary>The independent check of exported workbooks with the Open XML SDK.</summary>
internal static class ExportChecks
{
    /// <summary>Checks a successfully buffered Excel file with the Open XML SDK and rewinds it.</summary>
    public static BufferedExportResult Checked(
        BufferedExportResult result,
        bool excel
    )
    {
        if (excel
            && result is { Report.Status: ReportStatus.Succeeded, File: { } file })
        {
            Assert.Empty(TestFiles.WorkbookProblems(file));
            file.Position = 0;
        }

        return result;
    }

    /// <summary>
    /// Checks a workbook written successfully into a readable seekable stream and restores the stream position; a
    /// discarding target such as <see cref="Stream.Null" /> holds nothing to check.
    /// </summary>
    public static ExportReport Checked(
        ExportReport report,
        Stream target,
        bool excel
    )
    {
        if (excel
            && report.Status == ReportStatus.Succeeded
            && target is { CanSeek: true, CanRead: true, Length: > 0 })
        {
            var position = target.Position;
            Assert.Empty(TestFiles.WorkbookProblems(target));
            target.Position = position;
        }

        return report;
    }

    /// <summary>Checks a workbook published successfully at a path.</summary>
    public static ExportReport CheckedPath(
        ExportReport report,
        string path,
        bool excel
    )
    {
        if (excel
            && report.Status == ReportStatus.Succeeded)
        {
            using var file = File.OpenRead(path);
            Assert.Empty(TestFiles.WorkbookProblems(file));
        }

        return report;
    }
}
