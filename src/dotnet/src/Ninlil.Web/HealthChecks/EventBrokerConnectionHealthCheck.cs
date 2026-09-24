using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Events.Broker;

// Define our namespace.
namespace Ninlil.Web.HealthChecks;

/// <summary>
///     This class implements the "broker connection state" health check. Only
///     meaningful when <see cref="DnsServerEventPublisherOptions.Transport" /> actually names a
///     real broker -- <see cref="EventTransportEnum.InMemory" /> (the default) reports
///     <see cref="HealthStatus.Healthy" /> trivially, since delivery never leaves this process at
///     all in that mode.
///     <para>
///         <c>Ninlil.Web</c>'s own <c>Program.cs</c> references
///         <c>Ninlil.Dns.Server.Events.RabbitMq</c> and
///         <c>Ninlil.Dns.Server.Events.ActiveMq</c> directly, conditionally calling
///         whichever one's own extension matches
///         <see cref="DnsServerEventPublisherOptions.Transport" />, so a configured broker
///         actually takes effect in this host.
///         <see cref="IDnsServerEventBrokerTransport" /> can still be missing here only if
///         <see cref="DnsServerEventPublisherOptions.Transport" /> somehow names a value
///         outside this enum's own declared members (not reachable through ordinary
///         configuration binding); kept as a defensive fallback, reported as
///         <see cref="HealthStatus.Degraded" /> rather than assumed unreachable.
///     </para>
/// </summary>
/// <param name="serviceProvider">The provider this check resolves an optionally-registered <see cref="IDnsServerEventBrokerTransport" /> from.</param>
/// <param name="publisherOptions">This host's own configured event transport.</param>
public sealed class EventBrokerConnectionHealthCheck(
    IServiceProvider serviceProvider,
    IOptions<DnsServerEventPublisherOptions> publisherOptions
) : IHealthCheck
{
    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        EventTransportEnum transport = publisherOptions.Value.Transport;

        if (transport == EventTransportEnum.InMemory)
            return Task.FromResult(
                HealthCheckResult.Healthy("Event delivery stays in-process (no broker configured).")
            );

        IDnsServerEventBrokerTransport brokerTransport =
            serviceProvider.GetService<IDnsServerEventBrokerTransport>();

        if (brokerTransport is null)
            return Task.FromResult(
                HealthCheckResult.Degraded(
                    $"'{transport}' is configured as the event transport, but this host has no "
                        + "IDnsServerEventBrokerTransport registered -- events are silently "
                        + "falling back to in-process delivery (see this check's own doc comment)."
                )
            );

        return Task.FromResult(
            brokerTransport.IsConnected
                ? HealthCheckResult.Healthy($"Connected to the {transport} event broker.")
                : HealthCheckResult.Degraded(
                    $"Not currently connected to the {transport} event broker."
                )
        );
    }
}
