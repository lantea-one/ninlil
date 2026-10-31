using Microsoft.Extensions.Logging;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv4.Tests;

/// <summary>
///     This class is an <see cref="ILogger{TCategoryName}" /> that records every entry with its
///     level, so a test can count the errors a hosted service logs when its listener cannot
///     bind. The listener's bind and the hosted service log from one task, but a lock keeps the
///     list safe for a test that reads while another task writes.
/// </summary>
/// <typeparam name="T">The logging category, the class whose messages the test reads.</typeparam>
public sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];

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

    /// <summary>
    ///     This method returns the text of every entry logged at <paramref name="level" />.
    /// </summary>
    public List<string> Messages(LogLevel level)
    {
        lock (_entries)
            return
            [
                .. _entries.Where(entry => entry.Level == level).Select(entry => entry.Message),
            ];
    }
}
