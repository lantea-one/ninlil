using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events.Broker;

/// <summary>
///     This hosted service consumes envelopes from the configured
///     <see cref="IDnsServerEventBrokerTransport" /> (see
///     <see cref="DnsServerEventPublisherOptions.Transport" />). It drops an envelope this
///     process published (already delivered locally) and one it cannot decode (an unknown name
///     from a newer peer, or a type that must not cross the broker); every other envelope goes
///     into <see cref="DnsServerEventPublisher.EnqueueRemote" />, where only subscribers that
///     implement <see cref="IDnsServerRemoteEventSubscriber" /> see it. With the in-memory
///     transport it does nothing.
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
    /// <summary>
    ///     This field holds the unknown event names already warned about, so a newer peer's
    ///     event type is reported once per process.
    /// </summary>
    private readonly ConcurrentDictionary<string, byte> _warnedUnknownNames = new();

    /// <summary>
    ///     This method applies the origin filter and the decode rules to one received envelope.
    /// </summary>
    private void HandleEnvelope(DnsServerEventEnvelope envelope)
    {
        if (envelope.OriginInstanceId == publisher.InstanceId)
            return;

        if (!DnsServerEventEnvelopeCodec.TryDecode(envelope, out IDnsServerEvent domainEvent))
        {
            logger.LogDebug(
                "Ignored a {EventType} event from the message broker that this instance does not accept.",
                envelope.EventTypeName
            );

            if (
                envelope.EventTypeName is not null
                && _warnedUnknownNames.TryAdd(envelope.EventTypeName, 0)
            )
                logger.LogWarning(
                    "Ignored an event of type '{EventType}' from the message broker; it is unknown here or may not cross the broker. Upgrade every instance that shares a broker together.",
                    envelope.EventTypeName
                );

            return;
        }

        publisher.EnqueueRemote(domainEvent);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.Transport == EventTransportEnum.InMemory || brokerTransport is null)
            return;

        try
        {
            await brokerTransport.StartConsumingAsync(
                (envelope, _) =>
                {
                    HandleEnvelope(envelope);
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
            // The server keeps answering queries and publishing, but receives no events from
            // other instances until restarted. That is logged rather than stopping the host.
            logger.LogError(
                exception,
                "The message broker event consumer failed unexpectedly and will not retry automatically; this instance will no longer receive events from other instances until restarted."
            );
        }
    }
}
