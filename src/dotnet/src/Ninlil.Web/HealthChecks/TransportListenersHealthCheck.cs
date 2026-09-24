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
///     This class implements the "each enabled transport is actually listening"
///     health check -- Plain/DoT/DoQ/DnsCrypt sockets are genuinely bound and accepting, not
///     merely configured with <c>Enable: true</c> in <c>appsettings.json</c>. A transport that is
///     disabled entirely is simply omitted from this check's own description, not reported as
///     unhealthy.
///     <para>
///         Every real socket-based listener (<see cref="UdpDnsListener" />, <see cref="TcpDnsListener" />,
///         <see cref="DnsOverTlsListener" />, <see cref="DnsOverQuicListener" />,
///         <see cref="DnsCryptUdpListener" />, <see cref="DnsCryptTcpListener" />) exposes a real
///         <c>IsListening</c> property specifically for this check to consult -- the one place
///         each listener's own "am I bound and running" signal is surfaced. DNS-over-HTTPS is
///         deliberately different -- it is mapped directly onto this same host's own shared
///         Kestrel instance (see <c>MapNinlilDnsOverHttps</c>), so its own liveness signal is
///         simply <see cref="DnsOverHttpsOptions.Enable" /> itself: as long as this very endpoint
///         answered the request that reached this check, Kestrel is already listening.
///     </para>
///     <para>
///         A listener with configured listen addresses can be partly bound: some addresses
///         serving, one failing (not assigned to this host, taken by another program). Each
///         listener lists those in <c>FailedListenAddresses</c>; this check reports them as
///         <see cref="HealthStatus.Degraded" /> -- the transport still answers, so it isn't
///         unhealthy, but an operator needs to see that part of what they configured isn't being
///         served. A transport that bound nothing at all is still unhealthy.
///     </para>
/// </summary>
/// <param name="hostedServices">Every registered <see cref="IHostedService" /> -- each real listener is resolved from here by type.</param>
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

        // Plain DNS has no Enable flag of its own -- it always starts, per invariant #6.
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

        // DNS-over-HTTPS is mapped onto this same host's own shared Kestrel instance rather than
        // owning a socket of its own -- if this request reached this check at all, Kestrel is
        // already listening, so Enable is the only meaningful signal here.
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

        // Plain DNS has no Enable flag of its own and is always evaluated above, so `listening`
        // is never actually empty here -- at minimum it always contains Plain/UDP and Plain/TCP.
        return Task.FromResult(
            HealthCheckResult.Healthy(
                $"Every enabled transport is listening: {string.Join(", ", listening)}."
            )
        );
    }
}
