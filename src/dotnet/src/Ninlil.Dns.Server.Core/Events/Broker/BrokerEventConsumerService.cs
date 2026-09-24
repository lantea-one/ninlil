using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events.Broker;

/// <summary>
///     This class runs for the lifetime of the application, consuming events from the configured
///     <see cref="IDnsServerEventBrokerTransport" /> (when one is configured at all -- see
///     <see cref="DnsServerEventPublisherOptions.Transport" />) and feeding each one into
///     <see cref="DnsServerEventPublisher" />'s own local channel, the exact same channel
///     <see cref="DnsServerEventDispatcher" /> already drains for the in-memory transport. This
///     is what makes a broker-backed event delivered back to <em>this</em> instance's own
///     subscribers -- including for an event this same instance itself published -- without
///     <see cref="DnsServerEventDispatcher" /> needing to know a broker exists at all.
/// </summary>
/// <param name="publisher">The publisher whose local channel a received event is fed into.</param>
/// <param name="options">This service's own configured transport -- a no-op when it names <see cref="EventTransportEnum.InMemory" />.</param>
/// <param name="logger">The logger this service records connection/consume failures to.</param>
/// <param name="brokerTransport">The broker transport to consume from when configured.</param>
public sealed class BrokerEventConsumerService(
    DnsServerEventPublisher publisher,
    IOptions<DnsServerEventPublisherOptions> options,
    ILogger<BrokerEventConsumerService> logger,
    IDnsServerEventBrokerTransport brokerTransport = null
) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.Transport == EventTransportEnum.InMemory || brokerTransport is null)
            return;

        try
        {
            await brokerTransport.StartConsumingAsync(
                (domainEvent, _) =>
                {
                    publisher.EnqueueFromBroker(domainEvent);
                    return Task.CompletedTask;
                },
                stoppingToken
            );
        }
        catch (OperationCanceledException)
        {
            // Expected during a graceful shutdown.
        }
        catch (Exception exception)
        {
            // A consumer that can never (re)connect leaves this instance permanently unable to
            // see any other instance's own events -- serious, but not a reason to take the rest
            // of this server down; this instance can still answer queries and publish its own
            // events (which other instances will still receive), it just won't receive anyone
            // else's until restarted.
            logger.LogError(
                exception,
                "The message broker event consumer failed unexpectedly and will not retry automatically; this instance will no longer receive events from other instances until restarted."
            );
        }
    }
}
