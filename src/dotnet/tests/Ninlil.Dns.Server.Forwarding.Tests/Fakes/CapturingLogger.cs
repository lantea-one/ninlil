using Microsoft.Extensions.Logging;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Tests.Fakes;

/// <summary>
///     This class is an <see cref="ILogger{TCategoryName}" /> that keeps every entry with its
///     level, so a test can count the Warnings and Informations a component logged.
/// </summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    /// <summary>Every entry logged so far, formatted, in order.</summary>
    public IReadOnlyList<(LogLevel Level, string Message)> Entries
    {
        get
        {
            lock (_entries)
                return [.. _entries];
        }
    }

    /// <summary>
    ///     This method counts the entries logged at one level.
    /// </summary>
    /// <param name="level">The level to count.</param>
    public int Count(LogLevel level) => Entries.Count(entry => entry.Level == level);

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
            _entries.Add((logLevel, formatter(state, exception)));
    }
}
