using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

// Define our namespace.
namespace Ninlil.Dns.Server.Acme.Tests.Fakes;

/// <summary>
///     This class is an <see cref="ILogger{TCategoryName}" /> that keeps the level and the
///     formatted text of every entry, so a test can assert that one Warning named a server or that
///     a Debug entry was written.
/// </summary>
/// <typeparam name="TCategoryName">The category the logger is created for.</typeparam>
public sealed class CapturingLogger<TCategoryName> : ILogger<TCategoryName>
{
    /// <summary>The level and message of every entry logged, in order.</summary>
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    /// <inheritdoc />
    public IDisposable BeginScope<TState>(TState state) => NullLogger.Instance.BeginScope(state);

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception exception,
        Func<TState, Exception, string> formatter
    ) => Entries.Add((logLevel, formatter(state, exception)));
}
