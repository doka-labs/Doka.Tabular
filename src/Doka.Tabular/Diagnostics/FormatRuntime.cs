using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Doka.Tabular;

/// <summary>Resolved infrastructure of a format instance: limits, logging, and time.</summary>
internal sealed class FormatRuntime
{
    public FormatRuntime(
        TabularDiagnosticsOptions? diagnostics,
        TimeProvider? timeProvider,
        ResourceLimits limits,
        string category
    )
    {
        Limits = limits;
        Category = category;
        Time = timeProvider ?? TimeProvider.System;
        LoggerFactory = diagnostics?.LoggerFactory;
        Problems = [];
        if (diagnostics?.LoggerFactory is null)
        {
            Problems.Add(TabularError.Configuration(
                "The diagnostics settings need a logger factory; use NullLoggerFactory.Instance to disable logging."));
        }

        if (timeProvider is null)
        {
            Problems.Add(TabularError.Configuration("The time provider must not be null."));
        }

        Problems.AddRange(limits
            .Problems()
            .Select(problem => TabularError.Configuration(problem)));
    }

    public ResourceLimits Limits { get; }

    private string Category { get; }

    private TimeProvider Time { get; }

    private ILoggerFactory? LoggerFactory { get; }

    /// <summary>Gets configuration errors of the instance, reported by every call before the first read or write.</summary>
    public List<TabularError> Problems { get; }

    public OperationScope Scope(
        string direction,
        string format,
        string mode
    ) => new(LoggerFactory ?? NullLoggerFactory.Instance, Category, Time, direction, format, mode);
}
