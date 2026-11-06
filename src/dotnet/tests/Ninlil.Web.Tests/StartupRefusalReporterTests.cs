using Microsoft.Extensions.Logging;
using Ninlil.Dns.Data.Abstractions.Exceptions;
using Ninlil.Web.Startup;
using Xunit;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class tests <see cref="StartupRefusalReporter" /> without Windows: the service check
///     and the event log writer are passed in as fakes, so the tests run on every platform and
///     never touch a real event log. They do not join <c>WebTestCollection</c> because no host
///     is involved.
/// </summary>
public sealed class StartupRefusalReporterTests
{
    /// <summary>
    ///     This class records the entries written to it, or throws when asked to.
    /// </summary>
    private sealed class FakeEventLogWriter : IStartupRefusalEventLogWriter
    {
        public List<(string Source, string Message)> Entries { get; } = new();

        public bool Throw { get; init; }

        public void WriteError(string source, string message)
        {
            if (Throw)
                throw new InvalidOperationException("source not registered");
            Entries.Add((source, message));
        }
    }

    /// <summary>
    ///     This class records each logged level and message.
    /// </summary>
    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Text)> Lines { get; } = new();

        public IDisposable BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        ) => Lines.Add((logLevel, formatter(state, exception)));
    }

    private static DatabaseStartupRefusedException Refusal() =>
        new("DataProtection:CertificatePath names a file that cannot be opened.");

    /// <summary>
    ///     A refusal under a service without a host logger writes one event log entry under
    ///     <c>Ninlil.Web</c>, carrying the message.
    /// </summary>
    [Fact]
    public void Report_AsAWindowsServiceWithoutAHostLogger_WritesTheEventLogEntry()
    {
        FakeEventLogWriter writer = new();

        StartupRefusalReporter.Report(null, "x", Refusal(), true, writer);

        (string source, string message) = Assert.Single(writer.Entries);
        Assert.Equal("Ninlil.Web", source);
        Assert.Contains("DataProtection:CertificatePath", message);
    }

    /// <summary>
    ///     A refusal under a service with a host logger logs the critical line and writes no
    ///     explicit entry, because the host's event log provider records that line.
    /// </summary>
    [Fact]
    public void Report_AsAWindowsServiceWithAHostLogger_LogsAndWritesNoExplicitEntry()
    {
        RecordingLogger logger = new();
        FakeEventLogWriter writer = new();

        StartupRefusalReporter.Report(logger, "x", Refusal(), true, writer);

        Assert.Contains(
            logger.Lines,
            l => l.Level == LogLevel.Critical && l.Text.Contains("DataProtection:CertificatePath")
        );
        Assert.Empty(writer.Entries);
    }

    /// <summary>
    ///     A console run logs the message and never touches the event log.
    /// </summary>
    [Fact]
    public void Report_NotAsAWindowsService_LogsAndSkipsTheEventLog()
    {
        RecordingLogger logger = new();
        FakeEventLogWriter writer = new();

        StartupRefusalReporter.Report(logger, "x", Refusal(), false, writer);

        Assert.Contains(logger.Lines, l => l.Level == LogLevel.Critical);
        Assert.Empty(writer.Entries);
    }

    /// <summary>
    ///     A failing event log (an unregistered source, for example) does not throw.
    /// </summary>
    [Fact]
    public void Report_WhenTheEventLogFails_DoesNotThrow()
    {
        FakeEventLogWriter writer = new() { Throw = true };

        StartupRefusalReporter.Report(null, "x", Refusal(), true, writer);

        Assert.Empty(writer.Entries);
    }

    /// <summary>
    ///     Without a logger the call creates a console one and still does not throw.
    /// </summary>
    [Fact]
    public void Report_WithoutALogger_DoesNotThrow() =>
        StartupRefusalReporter.Report(
            null,
            "Ninlil.Web.Test",
            Refusal(),
            false,
            new FakeEventLogWriter()
        );
}
