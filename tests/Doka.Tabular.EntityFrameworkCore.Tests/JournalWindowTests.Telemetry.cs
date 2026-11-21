using System.Diagnostics;
using Doka.Tabular.Csv;
using Doka.Tabular.Excel;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Doka.Tabular.EntityFrameworkCore.Tests;

/// <summary>
/// A completion function that throws is a technical error of the call; the stored state stays successful, while the
/// completion log entry and the activity report the error.
/// </summary>
public sealed partial class JournalWindowTests
{
    public enum Entry
    {
        CsvImport,
        WorkbookImport,
        Export,
    }

    [Theory]
    [InlineData(Entry.CsvImport)]
    [InlineData(Entry.WorkbookImport)]
    [InlineData(Entry.Export)]
    public async Task ThrowingCompletionFunction_IsLoggedAndTracedAsAnError(
        Entry entry
    )
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync();
        var journal = TabularJournal.ForFactory(database.Plain);
        var logger = new LevelRecorder();
        var diagnostics = new TabularDiagnosticsOptions { LoggerFactory = logger };
        var activities = new List<Activity>();
        using var listener = new ActivityListener();
        listener.ShouldListenTo = source => source.Name == TabularDiagnostics.ActivitySourceName;
        listener.Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded;
        listener.ActivityStopped = activity =>
        {
            lock (activities)
            {
                activities.Add(activity);
            }
        };

        ActivitySource.AddActivityListener(listener);

        // Act
        var (status, errors, operation) = entry switch
        {
            Entry.Export => await ExportAsync(),
            _ => await ImportCallAsync(),
        };

        // Assert
        Activity activity;
        lock (activities)
        {
            activity = Assert.Single(activities,
                candidate => Equals(candidate.GetTagItem("doka.tabular.operation_id"), operation.ToString()));
        }

        Assert.Equal(ReportStatus.Succeeded, status);
        Assert.Contains(errors,
            error => error is { Code.Value: "tabular.callback.failed", Kind: TabularErrorKind.Technical });

        Assert.Equal(LogLevel.Error, logger.CompletedLevel);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);

        return;

        Func<TabularImportCompleted, CancellationToken, Task> ThrowingImport() => (_, _)
            => throw new InvalidOperationException("The completion failed.");

        async Task<(ReportStatus, IReadOnlyList<TabularError>, Guid)> ImportCallAsync()
        {
            // ReSharper disable once AccessToDisposedClosure
            // WHY: The local function runs during the awaited call, before the database is disposed.
            var target = TestMaps
                .Upsert(TabularDatabase.ForFactory(database.Plain))
                .WithImportLog(journal,
                    new TabularImportLogOptions
                    {
                        Scope = s_scope,
                        OnCompletedAsync = ThrowingImport(),
                    });

            if (entry == Entry.CsvImport)
            {
                var report = await new CsvFormat(new CsvFormatOptions { Diagnostics = diagnostics })
                    .ImportAsync(TestMaps.Csv("1,a@example.org,Ada"), TestMaps.Users, target, Token);

                return (report.Status, report.Errors, report.Diagnostics.OperationId);
            }

            var sheets = ExcelImport.Create();
            sheets.Add(TabularSheetName.Create("First"), TestMaps.Users, target);
            var workbook = await new ExcelFormat(new ExcelFormatOptions { Diagnostics = diagnostics }).ImportAsync(
                await WorkbookAsync([new UserDto(1, "a@example.org", "Ada")], []),
                sheets.Build(),
                Token);

            return (workbook.Status, workbook.Errors, workbook.Diagnostics.OperationId);
        }

        async Task<(ReportStatus, IReadOnlyList<TabularError>, Guid)> ExportAsync()
        {
            var target = TabularFileTarget
                .To(new MemoryFileStore(), "users.csv")
                .WithExportLog(journal,
                    new TabularExportLogOptions
                    {
                        Scope = s_scope,
                        OnCompletedAsync = (_, _) => throw new InvalidOperationException("The completion failed."),
                    });

            var report = await new CsvFormat(new CsvFormatOptions { Diagnostics = diagnostics }).ExportAsync(
                new[] { new UserDto(1, "a@example.org", "Ada") },
                TestMaps.Users,
                target,
                Token);

            return (report.Status, report.Errors, report.Diagnostics.OperationId);
        }
    }

    /// <summary>Keeps the level of the completion entry of a call.</summary>
    private sealed class LevelRecorder : ILoggerFactory, ILogger
    {
        public LogLevel? CompletedLevel { get; private set; }

        public IDisposable? BeginScope<TState>(
            TState state
        )
            where TState : notnull => null;

        public bool IsEnabled(
            LogLevel logLevel
        ) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (eventId.Id == TabularLogEvents.OperationCompleted.Id)
            {
                CompletedLevel = logLevel;
            }
        }

        public void AddProvider(
            ILoggerProvider provider
        ) { }

        public ILogger CreateLogger(
            string categoryName
        ) => this;

        public void Dispose() { }
    }
}
