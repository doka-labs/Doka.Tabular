using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Doka.Tabular;

/// <summary>Logging settings of a format instance or journal.</summary>
[PublicAPI]
public sealed record TabularDiagnosticsOptions
{
    /// <summary>Gets the logger factory; the default is <see cref="NullLoggerFactory.Instance" />. <see langword="null" /> is a configuration error.</summary>
    public ILoggerFactory? LoggerFactory { get; init; } = NullLoggerFactory.Instance;
}

/// <summary>Constants of the tracing integration.</summary>
[PublicAPI]
public static class TabularDiagnostics
{
    /// <summary>Name of the <see cref="ActivitySource" />: <c>Doka.Tabular</c>.</summary>
    public const string ActivitySourceName = "Doka.Tabular";

    internal static ActivitySource Source { get; } = new(ActivitySourceName);
}

/// <summary>Stable event identifiers of Doka.Tabular log events.</summary>
[PublicAPI]
public static class TabularLogEvents
{
    /// <summary>Gets event 1000, once per call before the first read or write.</summary>
    public static EventId OperationStarted { get; } = new(1000, nameof(OperationStarted));

    /// <summary>Gets event 1001, once per call with the report.</summary>
    public static EventId OperationCompleted { get; } = new(1001, nameof(OperationCompleted));

    /// <summary>Gets event 1002 for a technical termination.</summary>
    public static EventId OperationFailed { get; } = new(1002, nameof(OperationFailed));

    /// <summary>Gets event 1200 for an uncertain commit or rollback.</summary>
    public static EventId PersistenceUncertain { get; } = new(1200, nameof(PersistenceUncertain));

    /// <summary>Gets event 1300 when own cleanup failed.</summary>
    public static EventId CleanupFailed { get; } = new(1300, nameof(CleanupFailed));

    /// <summary>Gets event 1400 when a journal state was not written or is uncertain.</summary>
    public static EventId JournalUpdateFailed { get; } = new(1400, nameof(JournalUpdateFailed));

    /// <summary>Gets event 1401 when a completion callback threw or timed out.</summary>
    public static EventId CompletionFailed { get; } = new(1401, nameof(CompletionFailed));
}

internal static partial class TabularLog
{
    [LoggerMessage(
        EventId = 1000,
        EventName = "OperationStarted",
        Level = LogLevel.Debug,
        Message = "Tabular {Direction} {Format} started (operation {OperationId})")]
    public static partial void OperationStarted(
        ILogger logger,
        Guid operationId,
        string direction,
        string format
    );

    [LoggerMessage(
        EventId = 1001,
        EventName = "OperationCompleted",
        Message =
            "Tabular {Direction} {Format} completed with {Status} in {DurationMilliseconds} ms (operation {OperationId}, complete {IsComplete}, errors {TotalErrorCount}, omitted {OmittedErrorCount}, read {RowsRead}, imported {RowsImported}, rejected {RowsRejected}, skipped {RowsSkipped}, pending {RowsPendingCommit}, uncertain {RowsUncertain}, written {RowsWritten})")]
    public static partial void OperationCompleted(
        ILogger logger,
        LogLevel level,
        Guid operationId,
        string direction,
        string format,
        ReportStatus status,
        long durationMilliseconds,
        bool isComplete,
        long totalErrorCount,
        long omittedErrorCount,
        long rowsRead,
        long rowsImported,
        long rowsRejected,
        long rowsSkipped,
        long rowsPendingCommit,
        long rowsUncertain,
        long rowsWritten
    );

    [LoggerMessage(
        EventId = 1002,
        EventName = "OperationFailed",
        Level = LogLevel.Error,
        Message = "Tabular {Direction} {Format} failed technically with {Code} ({Kind}) (operation {OperationId})")]
    public static partial void OperationFailed(
        ILogger logger,
        Guid operationId,
        string direction,
        string format,
        TabularErrorCode code,
        TabularErrorKind kind
    );

    [LoggerMessage(
        EventId = 1200,
        EventName = "PersistenceUncertain",
        Level = LogLevel.Error,
        Message = "Tabular {Direction} {Format}: commit or rollback is uncertain (operation {OperationId})")]
    public static partial void PersistenceUncertain(
        ILogger logger,
        Guid operationId,
        string direction,
        string format
    );

    [LoggerMessage(
        EventId = 1300,
        EventName = "CleanupFailed",
        Level = LogLevel.Error,
        Message = "Tabular {Direction} {Format}: own cleanup failed (operation {OperationId})")]
    public static partial void CleanupFailed(
        ILogger logger,
        Guid operationId,
        string direction,
        string format
    );

    [LoggerMessage(
        EventId = 1400,
        EventName = "JournalUpdateFailed",
        Level = LogLevel.Error,
        Message = "Tabular {Direction} {Format}: journal state was not written (operation {OperationId})")]
    public static partial void JournalUpdateFailed(
        ILogger logger,
        Guid operationId,
        string direction,
        string format
    );

    [LoggerMessage(
        EventId = 1401,
        EventName = "CompletionFailed",
        Level = LogLevel.Error,
        Message = "Tabular {Direction} {Format}: completion callback failed (operation {OperationId})")]
    public static partial void CompletionFailed(
        ILogger logger,
        Guid operationId,
        string direction,
        string format
    );
}

/// <summary>Logging, tracing, and correlation of one call. Telemetry failures never affect processing.</summary>
internal sealed class OperationScope : IDisposable
{
    private readonly string _direction;
    private readonly string _format;
    private readonly long _started;
    private Activity? _activity;
    private ILogger? _logger;
    private IDisposable? _scope;

    public OperationScope(
        ILoggerFactory loggerFactory,
        string category,
        TimeProvider time,
        string direction,
        string format,
        string mode
    )
    {
        Time = time;
        _started = time.GetTimestamp();
        _direction = direction;
        _format = format;
        OperationId = Guid.NewGuid();
        Diagnostics = new TabularReportDiagnostics(OperationId);
        Safe(() =>
        {
            _logger = loggerFactory.CreateLogger(category);
        });

        Safe(() => _scope = _logger?.BeginScope(new Dictionary<string, object> { ["OperationId"] = OperationId }));
        try
        {
            _activity = TabularDiagnostics.Source.StartActivity($"tabular.{direction}");
            if (_activity is { IsAllDataRequested: true })
            {
                _activity.SetTag("doka.tabular.operation_id", OperationId.ToString());
                _activity.SetTag("doka.tabular.direction", direction);
                _activity.SetTag("doka.tabular.format", format);
                _activity.SetTag("doka.tabular.mode", mode);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _activity = null;
        }
    }

    /// <summary>Gets the clock of the call, used for journal times and the completion timeout.</summary>
    public TimeProvider Time { get; }

    public Guid OperationId { get; }

    public TabularReportDiagnostics Diagnostics { get; }

    public void Dispose()
    {
        Safe(() => _scope?.Dispose());
        try
        {
            _activity?.Dispose();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _activity = null;
        }
    }

    public void Started() => Log(logger => TabularLog.OperationStarted(logger, OperationId, _direction, _format));

    public void CleanupFailed() => Log(logger => TabularLog.CleanupFailed(logger, OperationId, _direction, _format));

    public void PersistenceUncertain()
        => Log(logger => TabularLog.PersistenceUncertain(logger, OperationId, _direction, _format));

    public void JournalUpdateFailed()
        => Log(logger => TabularLog.JournalUpdateFailed(logger, OperationId, _direction, _format));

    public void CompletionFailed()
        => Log(logger => TabularLog.CompletionFailed(logger, OperationId, _direction, _format));

    public void Completed(
        ReportStatus status,
        bool isComplete,
        TabularError? fatal,
        long totalErrors,
        long omittedErrors,
        ReportCounters counters,
        bool hasDataOrConfigurationErrors,
        bool hasTechnicalErrors
    )
    {
        // WHY: A technical error that did not end the run, such as a failing completion function, is still an error
        // of the operation, while the stored state may stay successful.
        var technical = hasTechnicalErrors || fatal is { Kind: TabularErrorKind.Technical };
        if (fatal is { Kind: TabularErrorKind.Technical } failure)
        {
            Log(logger => TabularLog.OperationFailed(
                logger,
                OperationId,
                _direction,
                _format,
                failure.Code,
                failure.Kind));
        }

        var level = LevelOf(technical, status, hasDataOrConfigurationErrors);

        var duration = (long)Time.GetElapsedTime(_started)
            .TotalMilliseconds;

        Log(logger => TabularLog.OperationCompleted(
            logger,
            level,
            OperationId,
            _direction,
            _format,
            status,
            duration,
            isComplete,
            totalErrors,
            omittedErrors,
            counters.RowsRead,
            counters.RowsImported,
            counters.RowsRejected,
            counters.RowsSkipped,
            counters.RowsPendingCommit,
            counters.RowsUncertain,
            counters.RowsWritten));

        TagActivity(status, isComplete, totalErrors, counters, technical);
    }

    /// <summary>Error for technical failures, information for clean ends, otherwise warning.</summary>
    private static LogLevel LevelOf(
        bool technical,
        ReportStatus status,
        bool hasDataOrConfigurationErrors
    ) => (technical, status) switch
    {
        (true, _) => LogLevel.Error,
        (false, ReportStatus.Succeeded) => LogLevel.Information,
        (false, ReportStatus.Canceled) when !hasDataOrConfigurationErrors => LogLevel.Information,
        (false, ReportStatus.Canceled or ReportStatus.CompletedWithErrors or ReportStatus.Failed) => LogLevel.Warning,
        (false, var undefined) => throw new UnreachableException($"The report status {undefined} is not defined."),
    };

    /// <summary>Adds the result to the activity; a failing listener never fails the operation.</summary>
    private void TagActivity(
        ReportStatus status,
        bool isComplete,
        long totalErrors,
        ReportCounters counters,
        bool technical
    )
    {
        try
        {
            if (_activity is { IsAllDataRequested: true } activity)
            {
                activity.SetTag("doka.tabular.status", status.ToString());
                activity.SetTag("doka.tabular.is_complete", isComplete);
                activity.SetTag("doka.tabular.error_count", totalErrors);
                activity.SetTag("doka.tabular.rows_read", counters.RowsRead);
                activity.SetTag("doka.tabular.rows_imported", counters.RowsImported);
                activity.SetTag("doka.tabular.rows_rejected", counters.RowsRejected);
                activity.SetTag("doka.tabular.rows_skipped", counters.RowsSkipped);
                activity.SetTag("doka.tabular.rows_pending_commit", counters.RowsPendingCommit);
                activity.SetTag("doka.tabular.rows_uncertain", counters.RowsUncertain);
                activity.SetTag("doka.tabular.rows_written", counters.RowsWritten);
            }

            if (status == ReportStatus.Failed || technical)
            {
                _activity?.SetStatus(ActivityStatusCode.Error);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _activity = null;
        }
    }

    private void Log(
        Action<ILogger> write
    )
    {
        if (_logger is not { } logger)
        {
            return;
        }

        try
        {
            write(logger);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // WHY: A failing logger must not change processing, commit, or report; the call stops logging.
            _logger = null;
        }
    }

    private void Safe(
        Action action
    )
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _logger = null;
        }
    }
}

internal readonly record struct ReportCounters(
    long RowsRead,
    long RowsImported,
    long RowsRejected,
    long RowsSkipped,
    long RowsPendingCommit,
    long RowsUncertain,
    long RowsWritten
);
