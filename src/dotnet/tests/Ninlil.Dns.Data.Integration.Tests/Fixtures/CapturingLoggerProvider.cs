using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Fixtures;

/// <summary>
///     This class is a logger provider that keeps every entry written through the loggers it
///     creates. A test registers it in the service collection of an <c>EngineDatabase</c> so it
///     sees what EF Core and the code under test log, and asserts on <see cref="Entries" />.
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLogEntry> _entries = new();

    /// <summary>
    ///     This property contains the entries logged so far, in order.
    /// </summary>
    public IReadOnlyList<CapturedLogEntry> Entries => [.. _entries];

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

    /// <inheritdoc />
    public void Dispose() { }

    /// <summary>
    ///     This class is the logger <see cref="CapturingLoggerProvider" /> hands out; it appends
    ///     each enabled entry to the provider's queue.
    /// </summary>
    private sealed class CapturingLogger(string category, ConcurrentQueue<CapturedLogEntry> entries)
        : ILogger
    {
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
        ) => entries.Enqueue(new CapturedLogEntry(category, logLevel, formatter(state, exception)));
    }
}

/// <summary>
///     This record is one entry a <see cref="CapturingLoggerProvider" /> recorded.
/// </summary>
/// <param name="Category">The logger category, usually the type that logged it.</param>
/// <param name="Level">The log level.</param>
/// <param name="Message">The formatted message.</param>
public sealed record CapturedLogEntry(string Category, LogLevel Level, string Message);
