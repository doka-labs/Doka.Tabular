using System.Diagnostics;
using Microsoft.Extensions.Logging;
using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Tests;

public sealed partial class BehaviorTests
{
    private static readonly TabularMap<Mail> s_mails = TabularMap
        .For<Mail>()
        .Column(x => x.Email, index: 0)
        .Build();

    [Fact]
    public async Task Logging_WritesStartAndCompletionWithTheOperationIdAndNoSourceValues()
    {
        // Arrange
        var logger = new CapturingLoggerFactory();
        var format =
            new CsvFormat(
                new CsvFormatOptions
                {
                    Diagnostics = new TabularDiagnosticsOptions { LoggerFactory = logger },
                });

        var map = TabularMap
            .For<Pair>()
            .Column(x => x.Email, index: 0)
            .Column(x => x.NumericId, index: 1, configure: c => c.IncludeSourceValue())
            .Build();

        // Act
        var report = await format.ImportAsync(
            TestInput.Utf8("a,secret1\nb,secret2\nc,3"),
            map,
            new CsvImportOptions { Strategy = ImportStrategy.Partial },
            Token);

        // Assert
        Assert.Equal(
            [TabularLogEvents.OperationStarted.Id, TabularLogEvents.OperationCompleted.Id],
            logger.Events.Select(entry => entry.Id.Id));

        Assert.Equal((LogLevel.Debug, LogLevel.Warning), (logger.Events[0].Level, logger.Events[1].Level));
        Assert.All(
            logger.Events,
            entry => Assert.Contains(
                entry.State,
                pair => pair.Key == "OperationId" && Equals(pair.Value, report.Diagnostics.OperationId)));

        Assert.All(logger.Events, entry => Assert.DoesNotContain("secret", entry.Text, StringComparison.Ordinal));
        Assert.Equal(ReportStatus.CompletedWithErrors, report.Status);
    }

    [Fact]
    public async Task Logging_TechnicalAbort_LogsTheFailureAndTheCompletionAsErrors()
    {
        // Arrange
        var logger = new CapturingLoggerFactory();
        var map = TabularMap
            .For<Pair>()
            .Column(x => x.Email, index: 0)
            .Column(x => x.NumericId, index: 1, configure: c => c.Validate(_ => throw new InvalidOperationException()))
            .Build();

        // Act
        await new CsvFormat(
            new CsvFormatOptions
            {
                Diagnostics = new TabularDiagnosticsOptions { LoggerFactory = logger },
            }).ImportAsync(TestInput.Utf8("a,1"), map, Token);

        // Assert
        Assert.Contains(
            logger.Events,
            entry => entry.Id.Id == TabularLogEvents.OperationFailed.Id && entry.Level == LogLevel.Error);

        Assert.Contains(
            logger.Events,
            entry => entry.Id.Id == TabularLogEvents.OperationCompleted.Id && entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Logging_ThrowingLogger_DoesNotChangeTheResult()
    {
        // Arrange
        var throwing = new CapturingLoggerFactory(throwOnLog: true);

        // Act
        var report = await new CsvFormat(
            new CsvFormatOptions
            {
                Diagnostics = new TabularDiagnosticsOptions { LoggerFactory = throwing },
            }).ImportAsync(TestInput.Utf8("a"), s_mails, Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
    }

    [Fact]
    public async Task Logging_MissingLoggerFactory_IsAConfigurationError()
    {
        // Act
        var report = await new CsvFormat(
            new CsvFormatOptions
            {
                Diagnostics = new TabularDiagnosticsOptions { LoggerFactory = null },
            }).ImportAsync(TestInput.Utf8("a"), s_mails, Token);

        // Assert
        Assert.Equal(
            TabularErrorCodes.Configuration.Invalid,
            Assert.Single(report.Errors)
                .Code);
    }

    [Fact]
    public async Task Tracing_CreatesOneActivityOnlyWithAListener()
    {
        // Arrange
        var map = TabularMap
            .For<Employee>()
            .Column(x => x.Email, index: 0)
            .Column(x => x.NumericId, index: 1)
            .Build();

        var activities = new List<Activity>();

        // Act
        // WHY: Only the comparison of a call without and a call with a listener shows that the listener creates the
        // activity, so both calls form one action.
        var before = await CsvInstance.ImportAsync(TestInput.Utf8("a,x"), map, Token);
        ImportReport<Employee> during;
        using (var listener = new ActivityListener())
        {
            // WHY: The listener sees the activities of every test class running in parallel, on any thread; only the
            // activity of this import counts.
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
            during = await CsvInstance.ImportAsync(TestInput.Utf8("a,x"), map, Token);
        }

        // Assert
        var operation = during.Diagnostics.OperationId.ToString();
        var activity = Assert.Single(
            activities,
            candidate => Equals(candidate.GetTagItem("doka.tabular.operation_id"), operation));

        Assert.DoesNotContain(
            activities,
            candidate => Equals(
                candidate.GetTagItem("doka.tabular.operation_id"),
                before.Diagnostics.OperationId.ToString()));

        Assert.Equal(("tabular.import", ActivityKind.Internal), (activity.OperationName, activity.Kind));
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal(ReportStatus.Failed, before.Status);
        Assert.Contains(activity.Tags, tag => tag.Key == "doka.tabular.status");
        Assert.All(
            [
                "rows_read", "rows_imported", "rows_rejected", "rows_skipped", "rows_pending_commit",
                "rows_uncertain", "rows_written",
            ],
            counter => Assert.NotNull(activity.GetTagItem($"doka.tabular.{counter}")));
    }
}
