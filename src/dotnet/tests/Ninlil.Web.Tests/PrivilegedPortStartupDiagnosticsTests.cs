using Microsoft.Extensions.Logging;
using Ninlil.Dns.Protocol.DnsOverQuic;
using Ninlil.Dns.Protocol.DnsOverTls;
using Ninlil.Dns.Protocol.Plain;
using Ninlil.Dns.Server.DnsCrypt;
using Ninlil.Web.Options;
using Ninlil.Web.Startup;
using Xunit;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class tests <see cref="PrivilegedPortStartupDiagnostics" /> without a host and without
///     depending on the privileges of the account running the tests.
///     <see cref="BuildConfiguredListeners_HonorsEachTransportsOwnEnableFlag" /> checks which
///     listeners count as configured, and the warning tests pass a
///     <see cref="FakePrivilegedPortBindProbe" /> with a fixed answer instead of asking whether
///     the process holds <c>CAP_NET_BIND_SERVICE</c>, which is why
///     <see cref="Ninlil.Web.Startup.IPrivilegedPortBindProbe" /> is an interface. The bind probe
///     itself, <see cref="SocketPrivilegedPortBindProbe" />, depends on the operating system's
///     privilege state and is not covered here.
/// </summary>
public sealed class PrivilegedPortStartupDiagnosticsTests
{
    /// <summary>
    ///     This method confirms that the configured listeners always include HTTP, HTTPS and
    ///     Plain DNS (<see cref="PlainProtocolOptions" /> has no <c>Enable</c> flag), and include
    ///     <see cref="DnsOverTlsOptions" />, <see cref="DnsOverQuicOptions" /> and
    ///     <see cref="DnsCryptOptions" /> only when their <c>Enable</c> flag is on, the same rule
    ///     <c>Program.cs</c> applies when it decides which transports start.
    /// </summary>
    [Fact]
    public void BuildConfiguredListeners_HonorsEachTransportsOwnEnableFlag()
    {
        WebListenOptions webListenOptions = new() { HttpPort = 80, HttpsPort = 443 };
        PlainProtocolOptions plainProtocolOptions = new() { Port = 53 };
        DnsOverTlsOptions dnsOverTlsOptions = new() { Enable = false, Port = 853 };
        DnsOverQuicOptions dnsOverQuicOptions = new() { Enable = false, Port = 853 };
        DnsCryptOptions dnsCryptOptions = new() { Enable = false, Port = 5353 };

        IReadOnlyList<PrivilegedPortListenerDescriptor> disabledResult =
            PrivilegedPortStartupDiagnostics.BuildConfiguredListeners(
                webListenOptions,
                plainProtocolOptions,
                dnsOverTlsOptions,
                dnsOverQuicOptions,
                dnsCryptOptions
            );

        // Only the three always-on listeners appear when every optional transport is disabled.
        Assert.Equal(3, disabledResult.Count);
        Assert.Contains(
            disabledResult,
            listener => listener is { TransportName: "HTTP", Port: 80 }
        );
        Assert.Contains(
            disabledResult,
            listener => listener is { TransportName: "HTTPS", Port: 443 }
        );
        Assert.Contains(
            disabledResult,
            listener => listener is { TransportName: "Plain DNS (UDP/TCP)", Port: 53 }
        );

        DnsOverTlsOptions enabledDnsOverTls = new() { Enable = true, Port = 853 };
        DnsOverQuicOptions enabledDnsOverQuic = new() { Enable = true, Port = 8530 };
        DnsCryptOptions enabledDnsCrypt = new() { Enable = true, Port = 5353 };

        IReadOnlyList<PrivilegedPortListenerDescriptor> enabledResult =
            PrivilegedPortStartupDiagnostics.BuildConfiguredListeners(
                webListenOptions,
                plainProtocolOptions,
                enabledDnsOverTls,
                enabledDnsOverQuic,
                enabledDnsCrypt
            );

        // Every optional transport now appears too, once enabled.
        Assert.Equal(6, enabledResult.Count);
        Assert.Contains(
            enabledResult,
            listener => listener is { TransportName: "DNS-over-TLS", Port: 853 }
        );
        Assert.Contains(
            enabledResult,
            listener => listener is { TransportName: "DNS-over-QUIC", Port: 8530 }
        );
        Assert.Contains(
            enabledResult,
            listener => listener is { TransportName: "DNSCrypt", Port: 5353 }
        );
    }

    /// <summary>
    ///     This method confirms that a privileged port (1023 or below) the probe reports as
    ///     unbindable produces one warning naming the transport and port.
    /// </summary>
    [Fact]
    public void WarnAboutUnbindablePrivilegedPorts_WarnsForAnUnbindablePrivilegedPort()
    {
        FakeLogger logger = new();
        FakePrivilegedPortBindProbe probe = new(unbindablePorts: [53]);
        PrivilegedPortListenerDescriptor[] listeners = [new("Plain DNS (UDP/TCP)", 53)];

        PrivilegedPortStartupDiagnostics.WarnAboutUnbindablePrivilegedPorts(
            listeners,
            probe,
            logger
        );

        Assert.Single(logger.WarningMessages);
        Assert.Contains("Plain DNS (UDP/TCP)", logger.WarningMessages[0]);
        Assert.Contains("53", logger.WarningMessages[0]);
    }

    /// <summary>
    ///     This method confirms that a privileged port the probe reports as bindable (for example
    ///     because the process holds <c>CAP_NET_BIND_SERVICE</c>) produces no warning; a low port
    ///     number alone must not trigger one.
    /// </summary>
    [Fact]
    public void WarnAboutUnbindablePrivilegedPorts_StaysSilentWhenTheProbeReportsItCanBind()
    {
        FakeLogger logger = new();
        FakePrivilegedPortBindProbe probe = new(unbindablePorts: []);
        PrivilegedPortListenerDescriptor[] listeners = [new("Plain DNS (UDP/TCP)", 53)];

        PrivilegedPortStartupDiagnostics.WarnAboutUnbindablePrivilegedPorts(
            listeners,
            probe,
            logger
        );

        Assert.Empty(logger.WarningMessages);
    }

    /// <summary>
    ///     This method confirms that a port above 1023 produces no warning even when the probe
    ///     reports it as unbindable, so only privileged ports are warned about.
    /// </summary>
    [Fact]
    public void WarnAboutUnbindablePrivilegedPorts_StaysSilentForANonPrivilegedPort()
    {
        FakeLogger logger = new();
        FakePrivilegedPortBindProbe probe = new(unbindablePorts: [5353]);
        PrivilegedPortListenerDescriptor[] listeners = [new("DNSCrypt", 5353)];

        PrivilegedPortStartupDiagnostics.WarnAboutUnbindablePrivilegedPorts(
            listeners,
            probe,
            logger
        );

        Assert.Empty(logger.WarningMessages);
    }

    /// <summary>
    ///     This class is a fake <see cref="IPrivilegedPortBindProbe" /> that reports a port as
    ///     unbindable only if it is in <paramref name="unbindablePorts" />.
    /// </summary>
    /// <param name="unbindablePorts">Every port this fake probe reports as unbindable.</param>
    private sealed class FakePrivilegedPortBindProbe(IReadOnlyCollection<int> unbindablePorts)
        : IPrivilegedPortBindProbe
    {
        /// <inheritdoc />
        public bool CanBind(int port) => !unbindablePorts.Contains(port);
    }

    /// <summary>
    ///     This class is a fake <see cref="ILogger" /> that records every message logged at
    ///     <see cref="LogLevel.Warning" />, so a test can check what the diagnostic told the
    ///     operator.
    /// </summary>
    private sealed class FakeLogger : ILogger
    {
        /// <summary>
        ///     This property contains the formatted text of every message logged at
        ///     <see cref="LogLevel.Warning" />, in order.
        /// </summary>
        public List<string> WarningMessages { get; } = [];

        /// <inheritdoc />
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

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
            if (logLevel == LogLevel.Warning)
                WarningMessages.Add(formatter(state, exception));
        }

        /// <summary>
        ///     This class is a no-op <see cref="IDisposable" /> scope, since the fake logger keeps
        ///     no scopes.
        /// </summary>
        private sealed class NullScope : IDisposable
        {
            /// <summary>
            ///     This property contains the shared instance <see cref="BeginScope{TState}" /> returns.
            /// </summary>
            public static readonly NullScope Instance = new();

            /// <inheritdoc />
            public void Dispose() { }
        }
    }
}
