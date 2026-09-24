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
///     This class tests <see cref="PrivilegedPortStartupDiagnostics" /> in full isolation from any
///     real OS-level privilege state: <see cref="BuildConfiguredListeners_HonorsEachTransportsOwnEnableFlag" />
///     and its siblings exercise the pure "which listeners are actually configured" logic
///     directly, and every "does this warn" test below supplies a
///     <see cref="FakePrivilegedPortBindProbe" /> with a deterministic, injected answer rather
///     than depending on whether the account actually running this test suite happens to have
///     <c>CAP_NET_BIND_SERVICE</c> or not -- exactly the reason
///     <see cref="Ninlil.Web.Startup.IPrivilegedPortBindProbe" /> exists as an interface in
///     the first place. The real, live bind-probe behavior of <see cref="SocketPrivilegedPortBindProbe" />
///     itself is inherently about genuine OS privilege state and is proven separately, by hand,
///     against a real, running process, per this project's own "a passing build is not evidence
///     anything works" convention for anything touching real runtime/OS behavior.
/// </summary>
public sealed class PrivilegedPortStartupDiagnosticsTests
{
    /// <summary>
    ///     This method confirms every configured listener this diagnostic actually reports
    ///     against reflects both <see cref="PlainProtocolOptions" />'s own lack of an
    ///     <c>Enable</c> flag (Plain DNS is always included) and each of
    ///     <see cref="DnsOverTlsOptions" />/<see cref="DnsOverQuicOptions" />/
    ///     <see cref="DnsCryptOptions" />'s own real <c>Enable</c> flag (each is included only
    ///     when actually turned on) -- the same gating <c>Program.cs</c>'s own composition root
    ///     already applies when it decides which transport actually starts.
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
    ///     This method confirms a privileged port (1023 or below) this process genuinely cannot
    ///     bind, per the injected probe, produces exactly one warning naming that transport.
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
    ///     This method confirms a privileged port this process genuinely can bind (for example
    ///     because it holds <c>CAP_NET_BIND_SERVICE</c>, per the injected probe's own answer)
    ///     produces no warning at all -- this diagnostic must never misfire purely off a low port
    ///     number.
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
    ///     This method confirms an ordinary, non-privileged port (above 1023) never produces a
    ///     warning, even if the injected probe would otherwise report it as unbindable -- the
    ///     privileged-port threshold gate must run before the probe is ever consulted at all.
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
    ///     This class implements <see cref="IPrivilegedPortBindProbe" /> with a deterministic,
    ///     in-memory answer -- a given port is reported unbindable only if it appears in
    ///     <paramref name="unbindablePorts" />, with every other port reported bindable.
    /// </summary>
    /// <param name="unbindablePorts">Every port this fake probe reports as unbindable.</param>
    private sealed class FakePrivilegedPortBindProbe(IReadOnlyCollection<int> unbindablePorts)
        : IPrivilegedPortBindProbe
    {
        /// <inheritdoc />
        public bool CanBind(int port) => !unbindablePorts.Contains(port);
    }

    /// <summary>
    ///     This class implements <see cref="ILogger" /> by recording every message logged at
    ///     <see cref="LogLevel.Warning" />, so a test can assert on exactly what this diagnostic
    ///     actually told an operator, without a real logging provider (console, a file, and so
    ///     on) in the loop at all.
    /// </summary>
    private sealed class FakeLogger : ILogger
    {
        /// <summary>
        ///     This property contains the fully formatted text of every message this logger has
        ///     recorded at <see cref="LogLevel.Warning" />, in the order each was logged.
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
        ///     This class implements a no-op <see cref="IDisposable" /> scope, since this fake
        ///     logger has no real scope stack to push onto or pop from.
        /// </summary>
        private sealed class NullScope : IDisposable
        {
            /// <summary>
            ///     This property contains the single, shared instance every <see cref="BeginScope{TState}" />
            ///     call returns.
            /// </summary>
            public static readonly NullScope Instance = new();

            /// <inheritdoc />
            public void Dispose() { }
        }
    }
}
