using Microsoft.Extensions.Logging;

namespace Doka.Tabular.Tests.Infrastructure;

/// <summary>A logger factory that records every event with its state, optionally throwing on each write.</summary>
internal sealed class CapturingLoggerFactory : ILoggerFactory
{
    private readonly bool _throwOnLog;

    public CapturingLoggerFactory(
        bool throwOnLog = false
    )
    {
        _throwOnLog = throwOnLog;
    }

    public List<(EventId Id, LogLevel Level, IReadOnlyList<KeyValuePair<string, object?>> State, string Text)> Events
    {
        get;
    } = [];

    public void AddProvider(
        ILoggerProvider provider
    ) { }

    public ILogger CreateLogger(
        string categoryName
    ) => new Logger(this);

    public void Dispose() { }

    private sealed class Logger : ILogger
    {
        private readonly CapturingLoggerFactory _owner;

        public Logger(
            CapturingLoggerFactory owner
        )
        {
            _owner = owner;
        }

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
            // WHY: The flag is readonly and set in the constructor; reading it needs no lock.
            // ReSharper disable once InconsistentlySynchronizedField
            if (_owner._throwOnLog)
            {
                throw new InvalidOperationException("logger failure");
            }

            lock (_owner.Events)
            {
                _owner.Events.Add((eventId, logLevel, state as IReadOnlyList<KeyValuePair<string, object?>> ?? [],
                    formatter(state, exception)));
            }
        }
    }
}
