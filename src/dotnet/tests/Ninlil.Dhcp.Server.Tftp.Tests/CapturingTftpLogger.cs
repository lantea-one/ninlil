using Microsoft.Extensions.Logging;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Tftp.Tests;

/// <summary>
///     This class collects every message <see cref="TftpServer" /> logs, at every level, so a
///     test can count and read the warnings. The server logs from several tasks, so reads and
///     writes take a lock.
/// </summary>
public sealed class CapturingTftpLogger : ILogger<TftpServer>
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    /// <inheritdoc />
    public IDisposable BeginScope<TState>(TState state) => null;

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
    ///     This method returns the text of every Warning logged so far.
    /// </summary>
    public List<string> Warnings()
    {
        lock (_entries)
            return [.. _entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message)];
    }

    /// <summary>
    ///     This method reports whether any message at the given level contains the text. Tests
    ///     poll it to learn that the server has handled a request.
    /// </summary>
    public bool Contains(LogLevel level, string text)
    {
        lock (_entries)
            return _entries.Any(e => e.Level == level && e.Message.Contains(text));
    }
}
