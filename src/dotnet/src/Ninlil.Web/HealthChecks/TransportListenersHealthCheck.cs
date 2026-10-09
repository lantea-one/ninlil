using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Protocol.DnsCrypt;
using Ninlil.Dns.Protocol.DnsOverHttps;
using Ninlil.Dns.Protocol.DnsOverQuic;
using Ninlil.Dns.Protocol.DnsOverTls;
using Ninlil.Dns.Protocol.Plain;
using Ninlil.Dns.Server.DnsCrypt;

// Define our namespace.
namespace Ninlil.Web.HealthChecks;

/// <summary>
///     This class implements the <c>transports</c> health check of <c>GET /status</c>: whether
///     each enabled DNS transport has its sockets bound, not just <c>Enable: true</c> in the
///     configuration. A transport that is not enabled is left out rather than reported as
///     unhealthy. The <c>Enable</c> flags are read from <see cref="IOptionsMonitor{TOptions}" />,
///     so a transport turned on or off through <c>TransportSettingsController</c> is judged by
///     its current setting. A transport that is enabled in the configuration but stopped through
///     <c>/api/services</c> reports as not listening, which is true.
///     <para>
///         Each socket listener (<see cref="UdpDnsListener" />, <see cref="TcpDnsListener" />,
///         <see cref="DnsOverTlsListener" />, <see cref="DnsOverQuicListener" />,
///         <see cref="DnsCryptUdpListener" />, <see cref="DnsCryptTcpListener" />) reports
///         <c>IsListening</c>, which this check reads. DNS-over-HTTPS has no socket of its own; it
///         is mapped onto the shared Kestrel listener (see <c>MapNinlilDnsOverHttps</c>), so it
///         counts as listening when <see cref="DnsOverHttpsOptions.Enable" /> is set and
///         <see cref="DnsOverHttpsTransportGate.IsRunning" /> is true.
///     </para>
///     <para>
///         A listener with configured listen addresses can be partly bound: some addresses
///         serving, one failing (not assigned to this host, taken by another program). Each
///         listener lists those in <c>FailedListenAddresses</c>; this check reports them as
///         <see cref="HealthStatus.Degraded" />: the transport still answers, but part of what was
///         configured is not served. A transport that bound nothing is unhealthy.
///     </para>
/// </summary>
/// <param name="hostedServices">Every registered <see cref="IHostedService" />; each listener is found by type.</param>
/// <param name="dnsOverTlsOptions">Whether DNS-over-TLS is enabled.</param>
/// <param name="dnsOverQuicOptions">Whether DNS-over-QUIC is enabled.</param>
/// <param name="dnsOverHttpsOptions">Whether DNS-over-HTTPS is enabled.</param>
/// <param name="dnsOverHttpsGate">The gate that says whether DNS-over-HTTPS is serving.</param>
/// <param name="dnsCryptOptions">Whether DNSCrypt is enabled.</param>
public sealed class TransportListenersHealthCheck(
    IEnumerable<IHostedService> hostedServices,
    IOptionsMonitor<DnsOverTlsOptions> dnsOverTlsOptions,
    IOptionsMonitor<DnsOverQuicOptions> dnsOverQuicOptions,
    IOptionsMonitor<DnsOverHttpsOptions> dnsOverHttpsOptions,
    DnsOverHttpsTransportGate dnsOverHttpsGate,
    IOptionsMonitor<DnsCryptOptions> dnsCryptOptions
) : IHealthCheck
{
    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        IReadOnlyList<IHostedService> services = [.. hostedServices];
        List<string> notListening = [];
        List<string> listening = [];
        List<string> partlyBound = [];

        void Evaluate(
            string name,
            bool enabled,
            bool isListening,
            IReadOnlyList<string> failedListenAddresses = null
        )
        {
            if (!enabled)
                return;

            if (!isListening)
            {
                notListening.Add(name);
                return;
            }

            listening.Add(name);

            if (failedListenAddresses is { Count: > 0 })
                partlyBound.Add(
                    $"{name} is not serving {string.Join(", ", failedListenAddresses)}"
                );
        }

        // Plain DNS has no Enable flag; it always starts.
        UdpDnsListener udpListener = services.OfType<UdpDnsListener>().FirstOrDefault();
        TcpDnsListener tcpListener = services.OfType<TcpDnsListener>().FirstOrDefault();
        Evaluate(
            "Plain/UDP",
            enabled: true,
            udpListener?.IsListening ?? false,
            udpListener?.FailedListenAddresses
        );
        Evaluate(
            "Plain/TCP",
            enabled: true,
            tcpListener?.IsListening ?? false,
            tcpListener?.FailedListenAddresses
        );

        DnsOverTlsListener dnsOverTlsListener = services
            .OfType<DnsOverTlsListener>()
            .FirstOrDefault();
        Evaluate(
            "DNS-over-TLS",
            dnsOverTlsOptions.CurrentValue.Enable,
            dnsOverTlsListener?.IsListening ?? false,
            dnsOverTlsListener?.FailedListenAddresses
        );

        DnsOverQuicListener dnsOverQuicListener = services
            .OfType<DnsOverQuicListener>()
            .FirstOrDefault();
        Evaluate(
            "DNS-over-QUIC",
            dnsOverQuicOptions.CurrentValue.Enable,
            dnsOverQuicListener?.IsListening ?? false,
            dnsOverQuicListener?.FailedListenAddresses
        );

        DnsCryptUdpListener dnsCryptUdpListener = services
            .OfType<DnsCryptUdpListener>()
            .FirstOrDefault();
        DnsCryptTcpListener dnsCryptTcpListener = services
            .OfType<DnsCryptTcpListener>()
            .FirstOrDefault();
        Evaluate(
            "DNSCrypt/UDP",
            dnsCryptOptions.CurrentValue.Enable,
            dnsCryptUdpListener?.IsListening ?? false,
            dnsCryptUdpListener?.FailedListenAddresses
        );
        Evaluate(
            "DNSCrypt/TCP",
            dnsCryptOptions.CurrentValue.Enable,
            dnsCryptTcpListener?.IsListening ?? false,
            dnsCryptTcpListener?.FailedListenAddresses
        );

        // DNS-over-HTTPS shares Kestrel, which is listening if this request arrived; the gate says
        // whether the transport itself is serving.
        Evaluate(
            "DNS-over-HTTPS",
            dnsOverHttpsOptions.CurrentValue.Enable,
            dnsOverHttpsGate.IsRunning
        );

        if (notListening.Count > 0)
            return Task.FromResult(
                HealthCheckResult.Unhealthy(
                    $"{notListening.Count} enabled transport(s) are not listening: "
                        + string.Join(", ", notListening)
                        + $". Listening: {(listening.Count > 0 ? string.Join(", ", listening) : "none")}."
                )
            );

        if (partlyBound.Count > 0)
            return Task.FromResult(
                HealthCheckResult.Degraded(
                    $"Every enabled transport is listening, but some configured listen addresses are not being served: {string.Join("; ", partlyBound)}."
                )
            );

        // Plain DNS is always evaluated and, having got here, is listening, so `listening` holds
        // at least Plain/UDP and Plain/TCP.
        return Task.FromResult(
            HealthCheckResult.Healthy(
                $"Every enabled transport is listening: {string.Join(", ", listening)}."
            )
        );
    }
}
