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
///     unhealthy. The <c>Enable</c> flags come from the settings loaded at startup, so a
///     transport turned on or off since then through <c>TransportSettingsController</c> is judged
///     by its startup setting.
///     <para>
///         Each socket listener (<see cref="UdpDnsListener" />, <see cref="TcpDnsListener" />,
///         <see cref="DnsOverTlsListener" />, <see cref="DnsOverQuicListener" />,
///         <see cref="DnsCryptUdpListener" />, <see cref="DnsCryptTcpListener" />) reports
///         <c>IsListening</c>, which this check reads. DNS-over-HTTPS has no socket of its own; it
///         is mapped onto the shared Kestrel listener (see <c>MapNinlilDnsOverHttps</c>), which is
///         listening if this request arrived, so the check uses
///         <see cref="DnsOverHttpsOptions.Enable" /> alone and does not see its transport gate.
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
/// <param name="dnsCryptOptions">Whether DNSCrypt is enabled.</param>
public sealed class TransportListenersHealthCheck(
    IEnumerable<IHostedService> hostedServices,
    IOptions<DnsOverTlsOptions> dnsOverTlsOptions,
    IOptions<DnsOverQuicOptions> dnsOverQuicOptions,
    IOptions<DnsOverHttpsOptions> dnsOverHttpsOptions,
    IOptions<DnsCryptOptions> dnsCryptOptions
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
            dnsOverTlsOptions.Value.Enable,
            dnsOverTlsListener?.IsListening ?? false,
            dnsOverTlsListener?.FailedListenAddresses
        );

        DnsOverQuicListener dnsOverQuicListener = services
            .OfType<DnsOverQuicListener>()
            .FirstOrDefault();
        Evaluate(
            "DNS-over-QUIC",
            dnsOverQuicOptions.Value.Enable,
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
            dnsCryptOptions.Value.Enable,
            dnsCryptUdpListener?.IsListening ?? false,
            dnsCryptUdpListener?.FailedListenAddresses
        );
        Evaluate(
            "DNSCrypt/TCP",
            dnsCryptOptions.Value.Enable,
            dnsCryptTcpListener?.IsListening ?? false,
            dnsCryptTcpListener?.FailedListenAddresses
        );

        // DNS-over-HTTPS shares Kestrel, which is listening if this request arrived, so Enable is
        // the only signal used.
        if (dnsOverHttpsOptions.Value.Enable)
            listening.Add("DNS-over-HTTPS (shared Kestrel)");

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
