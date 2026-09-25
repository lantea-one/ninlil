using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events.Broker;

/// <summary>
///     This hosted service consumes events from the configured
///     <see cref="IDnsServerEventBrokerTransport" /> (see
///     <see cref="DnsServerEventPublisherOptions.Transport" />) and puts each one into the local
///     channel of <see cref="DnsServerEventPublisher" />, which
///     <see cref="DnsServerEventDispatcher" /> drains. This is how broker events, including ones
///     this instance published, reach local subscribers. With the in-memory transport it does
///     nothing.
/// </summary>
/// <param name="publisher">The publisher whose local channel a received event is fed into.</param>
/// <param name="options">The configured transport; <see cref="EventTransportEnum.InMemory" /> makes this service do nothing.</param>
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
            // The server keeps answering queries and publishing, but receives no events (not
            // even its own) until restarted. That is logged rather than stopping the host.
            logger.LogError(
                exception,
                "The message broker event consumer failed unexpectedly and will not retry automatically; this instance will no longer receive events from other instances until restarted."
            );
        }
    }
}
