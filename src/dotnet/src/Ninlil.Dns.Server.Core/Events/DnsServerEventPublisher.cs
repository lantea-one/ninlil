using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.Events.Broker;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This class implements <see cref="IDnsServerEventPublisher" /> by writing to a bounded
///     in-memory channel that <see cref="DnsServerEventDispatcher" /> drains. One singleton is
///     registered under this type and the interface, so the dispatcher can reach the channel
///     while other code uses the interface.
///     <para>
///         When <see cref="DnsServerEventPublisherOptions.Transport" /> names a broker,
///         <see cref="Publish" /> sends the event to <paramref name="brokerTransport" /> instead.
///         Local subscribers then only see it when
///         <see cref="Broker.BrokerEventConsumerService" /> receives it back from the broker and
///         calls <see cref="EnqueueFromBroker" />, so the dispatcher works the same for every
///         transport.
///     </para>
/// </summary>
/// <param name="options">The queue capacity and transport, read at startup.</param>
/// <param name="brokerTransport">
///     The broker transport to publish through when configured, or <see langword="null" /> in
///     the default in-memory-only configuration.
/// </param>
/// <param name="logger">The logger this publisher records a failed broker publish to.</param>
public sealed class DnsServerEventPublisher(
    IOptions<DnsServerEventPublisherOptions> options,
    ILogger<DnsServerEventPublisher> logger,
    IDnsServerEventBrokerTransport brokerTransport = null
) : IDnsServerEventPublisher
{
    /// <summary>
    ///     This field contains the channel events wait in until
    ///     <see cref="DnsServerEventDispatcher" /> delivers them. When full, the oldest event is
    ///     dropped.
    /// </summary>
    private readonly Channel<IDnsServerEvent> _channel = Channel.CreateBounded<IDnsServerEvent>(
        new BoundedChannelOptions(options.Value.Capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
        }
    );

    /// <summary>
    ///     This property contains the reader <see cref="DnsServerEventDispatcher" /> drains.
    /// </summary>
    internal ChannelReader<IDnsServerEvent> Reader => _channel.Reader;

    /// <inheritdoc />
    public void Publish(IDnsServerEvent domainEvent)
    {
        if (options.Value.Transport == EventTransportEnum.InMemory || brokerTransport is null)
        {
            _channel.Writer.TryWrite(domainEvent);
            return;
        }

        // Not awaited, so the query path never waits on the broker. A failed publish is logged
        // and not retried, and local subscribers never see that event.
        _ = PublishToBrokerAsync(domainEvent);
    }

    /// <summary>
    ///     This method puts an event received from the broker into the local channel. Only
    ///     <see cref="Broker.BrokerEventConsumerService" /> calls it.
    /// </summary>
    internal void EnqueueFromBroker(IDnsServerEvent domainEvent) =>
        _channel.Writer.TryWrite(domainEvent);

    private async Task PublishToBrokerAsync(IDnsServerEvent domainEvent)
    {
        try
        {
            await brokerTransport.PublishAsync(domainEvent, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to publish a {EventType} event to the configured message broker.",
                domainEvent.GetType().Name
            );
        }
    }
}
