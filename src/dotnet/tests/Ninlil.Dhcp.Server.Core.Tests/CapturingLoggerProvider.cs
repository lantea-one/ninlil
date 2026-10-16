using Microsoft.Extensions.Logging;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Tests;

/// <summary>
///     This class is a logger provider that records every Warning (or worse) message. The engine
///     test hosts register one so a test can count the warnings an option overlay logs when it
///     skips a stored row. It is thread safe because the engine logs from request handlers.
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly object _lock = new();
    private readonly List<string> _warnings = [];

    /// <summary>
    ///     This property contains a snapshot of the Warning-or-worse messages logged so far.
    /// </summary>
    public IReadOnlyList<string> Warnings
    {
        get
        {
            lock (_lock)
                return [.. _warnings];
        }
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

    /// <inheritdoc />
    public void Dispose() { }

    /// <summary>
    ///     This class forwards each Warning-or-worse entry to its provider.
    /// </summary>
    private sealed class CapturingLogger(CapturingLoggerProvider provider) : ILogger
    {
        /// <inheritdoc />
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => null;

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        /// <inheritdoc />
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        )
        {
            if (!IsEnabled(logLevel))
                return;

            lock (provider._lock)
                provider._warnings.Add(formatter(state, exception));
        }
    }
}
