using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Events.Broker;

// Define our namespace.
namespace Ninlil.Web.HealthChecks;

/// <summary>
///     This class implements the <c>eventBroker</c> health check of <c>GET /status</c>: whether
///     the configured message broker is connected. With
///     <see cref="EventTransportEnum.InMemory" />, the default, events never leave the process,
///     so it reports <see cref="HealthStatus.Healthy" />.
///     <para>
///         <c>Program.cs</c> registers the RabbitMQ or ActiveMQ transport that
///         <see cref="DnsServerEventPublisherOptions.Transport" /> names, so an
///         <see cref="IDnsServerEventBrokerTransport" /> is missing only if the setting holds a
///         value outside the enum, which configuration binding does not produce. That case is
///         still handled and reported as <see cref="HealthStatus.Degraded" />.
///     </para>
/// </summary>
/// <param name="serviceProvider">Resolves the optional <see cref="IDnsServerEventBrokerTransport" />.</param>
/// <param name="publisherOptions">The configured event transport, from the settings loaded at startup.</param>
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
