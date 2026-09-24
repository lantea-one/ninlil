using Microsoft.Extensions.Logging;

// Define our namespace.
namespace Ninlil.Dns.Server.Filtering.Tests.Fakes;

/// <summary>
///     This class is an <see cref="ILogger{TCategoryName}" /> that keeps every formatted message,
///     so a test can assert on what a service reported.
/// </summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<string> _messages = [];

    /// <summary>Every message logged so far, formatted, in order.</summary>
    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_messages)
                return [.. _messages];
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
        lock (_messages)
            _messages.Add(formatter(state, exception));
    }
}
