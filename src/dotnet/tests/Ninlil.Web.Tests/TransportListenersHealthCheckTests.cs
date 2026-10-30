using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Protocol.DnsCrypt;
using Ninlil.Dns.Protocol.DnsOverHttps;
using Ninlil.Dns.Protocol.DnsOverQuic;
using Ninlil.Dns.Protocol.DnsOverTls;
using Ninlil.Dns.Protocol.Plain;
using Ninlil.Dns.Server.DnsCrypt;
using Ninlil.Web.HealthChecks;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class checks that <see cref="TransportListenersHealthCheck" /> follows live option
///     changes and the DNS-over-HTTPS transport gate. The check is constructed directly with no
///     hosted services, so the plain DNS listeners count as not listening in every test; the
///     assertions look only at the transport under test.
/// </summary>
public sealed class TransportListenersHealthCheckTests
{
    /// <summary>
    ///     This class is a minimal <see cref="IOptionsMonitor{TOptions}" /> whose current value
    ///     the test can replace, which stands in for a live settings change.
    /// </summary>
    private sealed class MutableMonitor<T>(T value) : IOptionsMonitor<T>
    {
        /// <inheritdoc />
        public T CurrentValue { get; set; } = value;

        /// <inheritdoc />
        public T Get(string name) => CurrentValue;

        /// <inheritdoc />
        public IDisposable OnChange(Action<T, string> listener) => null;
    }

    [Fact]
    public async Task CheckHealthAsync_AfterDnsOverTlsIsEnabledLive_ReportsItNotListening()
    {
        MutableMonitor<DnsOverTlsOptions> tls = new(new DnsOverTlsOptions { Enable = false });
        TransportListenersHealthCheck check = new(
            [],
            tls,
            new MutableMonitor<DnsOverQuicOptions>(new DnsOverQuicOptions()),
            new MutableMonitor<DnsOverHttpsOptions>(new DnsOverHttpsOptions()),
            new DnsOverHttpsTransportGate(),
            new MutableMonitor<DnsCryptOptions>(new DnsCryptOptions())
        );

        HealthCheckResult before = await check.CheckHealthAsync(new HealthCheckContext());
        Assert.DoesNotContain("DNS-over-TLS", before.Description);

        tls.CurrentValue = new DnsOverTlsOptions { Enable = true };

        HealthCheckResult after = await check.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, after.Status);
        Assert.Contains("DNS-over-TLS", after.Description);
    }

    [Fact]
    public async Task CheckHealthAsync_WithDnsOverHttpsEnabledAndGateStopped_ReportsItNotListening()
    {
        TransportListenersHealthCheck check = new(
            [],
            new MutableMonitor<DnsOverTlsOptions>(new DnsOverTlsOptions()),
            new MutableMonitor<DnsOverQuicOptions>(new DnsOverQuicOptions()),
            new MutableMonitor<DnsOverHttpsOptions>(new DnsOverHttpsOptions { Enable = true }),
            new DnsOverHttpsTransportGate(),
            new MutableMonitor<DnsCryptOptions>(new DnsCryptOptions())
        );

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("DNS-over-HTTPS", result.Description.Split("Listening:")[0]);
    }

    [Fact]
    public async Task CheckHealthAsync_WithDnsOverHttpsEnabledAndGateStarted_ReportsItListening()
    {
        DnsOverHttpsTransportGate gate = new();
        await gate.StartAsync(CancellationToken.None);
        TransportListenersHealthCheck check = new(
            [],
            new MutableMonitor<DnsOverTlsOptions>(new DnsOverTlsOptions()),
            new MutableMonitor<DnsOverQuicOptions>(new DnsOverQuicOptions()),
            new MutableMonitor<DnsOverHttpsOptions>(new DnsOverHttpsOptions { Enable = true }),
            gate,
            new MutableMonitor<DnsCryptOptions>(new DnsCryptOptions())
        );

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.DoesNotContain("DNS-over-HTTPS", result.Description.Split("Listening:")[0]);
        Assert.Contains("DNS-over-HTTPS", result.Description.Split("Listening:")[1]);
    }

    [Fact]
    public async Task CheckHealthAsync_WithAPlainListenerThatCouldNotBind_NamesTheAddressAndTheReason()
    {
        // Another socket holds the wildcard UDP port, so the listener's bind fails. The listener
        // must end without throwing and the check must say why it is not listening.
        using Socket occupant = new(
            AddressFamily.InterNetworkV6,
            SocketType.Dgram,
            ProtocolType.Udp
        );
        occupant.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
        occupant.Bind(new IPEndPoint(IPAddress.IPv6Any, 0));
        int port = ((IPEndPoint)occupant.LocalEndPoint).Port;

        // The pipeline is never used: the bind fails before the first receive.
        UdpDnsListener udp = new(
            null,
            new MutableMonitor<PlainProtocolOptions>(new PlainProtocolOptions { Port = port }),
            NullLogger<UdpDnsListener>.Instance
        );
        await udp.StartAsync(CancellationToken.None);
        await Task.WhenAny(udp.ExecuteTask, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.False(udp.ExecuteTask.IsFaulted);

        TransportListenersHealthCheck check = new(
            [udp],
            new MutableMonitor<DnsOverTlsOptions>(new DnsOverTlsOptions()),
            new MutableMonitor<DnsOverQuicOptions>(new DnsOverQuicOptions()),
            new MutableMonitor<DnsOverHttpsOptions>(new DnsOverHttpsOptions()),
            new DnsOverHttpsTransportGate(),
            new MutableMonitor<DnsCryptOptions>(new DnsCryptOptions())
        );

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("Plain/UDP (:: (", result.Description);
        Assert.Contains("in use", result.Description);
    }
}
