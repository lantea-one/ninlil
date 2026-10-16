using Microsoft.Extensions.Logging;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Relay.Tests;

/// <summary>
///     This class is a test logger that counts the entries logged at Warning level and keeps
///     their formatted text, so a test can assert that a fallback is reported exactly once.
///     <see cref="Dhcpv4RelayAgentTests" /> and <see cref="Dhcpv6RelayAgentTests" /> use it.
/// </summary>
/// <typeparam name="T">The category type, the relay agent under test.</typeparam>
internal sealed class WarningCountingLogger<T> : ILogger<T>
{
    private readonly List<string> _warnings = [];

    /// <summary>This property contains the formatted text of every Warning logged so far.</summary>
    public IReadOnlyList<string> Warnings
    {
        get
        {
            lock (_warnings)
                return [.. _warnings];
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
        if (logLevel != LogLevel.Warning)
            return;

        lock (_warnings)
            _warnings.Add(formatter(state, exception));
    }
}
