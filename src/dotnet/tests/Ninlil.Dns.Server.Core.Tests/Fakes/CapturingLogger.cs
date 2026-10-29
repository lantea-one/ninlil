using Microsoft.Extensions.Logging;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Tests.Fakes;

/// <summary>
///     This class is an <see cref="ILogger{TCategoryName}" /> that keeps every formatted message
///     with its level, so a test can assert on what was reported and how loudly.
/// </summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];
    private readonly List<Exception> _exceptions = [];

    /// <summary>Every entry logged so far, in order.</summary>
    public IReadOnlyList<(LogLevel Level, string Message)> Entries
    {
        get
        {
            lock (_entries)
                return [.. _entries];
        }
    }

    /// <summary>Every message logged at <see cref="LogLevel.Error" />.</summary>
    public IReadOnlyList<string> Errors =>
        [.. Entries.Where(entry => entry.Level == LogLevel.Error).Select(entry => entry.Message)];

    /// <summary>Every exception object passed to the logger so far, in order.</summary>
    public IReadOnlyList<Exception> Exceptions
    {
        get
        {
            lock (_entries)
                return [.. _exceptions];
        }
    }

    /// <inheritdoc />
    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception exception,
        Func<TState, Exception, string> formatter
    )
    {
        lock (_entries)
        {
            _entries.Add((logLevel, formatter(state, exception)));

            if (exception is not null)
                _exceptions.Add(exception);
        }
    }
}
