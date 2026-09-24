using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.Events.Broker;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This class implements <see cref="IDnsServerEventPublisher" /> by writing to an in-memory
///     channel that <see cref="DnsServerEventDispatcher" /> drains in the background. It is
///     registered in dependency injection under both this concrete type and the
///     <see cref="IDnsServerEventPublisher" /> interface, pointing at the same singleton
///     instance, so that most code depends only on the interface while
///     <see cref="DnsServerEventDispatcher" /> can still reach the channel this class owns.
///     <para>
///         When <see cref="DnsServerEventPublisherOptions.Transport" /> names a real broker,
///         <see cref="Publish" /> hands the event to
///         <paramref name="brokerTransport" /> instead of writing directly to the local channel
///         -- delivery to this instance's own subscribers then only happens once
///         <see cref="Broker.BrokerEventConsumerService" /> receives that same event back from
///         the broker and calls <see cref="EnqueueFromBroker" />, so
///         <see cref="DnsServerEventDispatcher" /> itself needs no changes at all regardless of
///         which transport is active.
///     </para>
/// </summary>
/// <param name="options">The queue capacity and configured transport.</param>
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
    ///     This property contains the channel published events wait in until
    ///     <see cref="DnsServerEventDispatcher" /> delivers them.
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

        // Fire-and-forget, matching this method's own "deliberately synchronous and
        // non-blocking" contract -- a broker round trip must never make the hot
        // query-answering path wait on network I/O. A failed publish is logged, not retried;
        // losing an occasional event under a broker outage is the same posture the in-memory
        // channel already has under sustained overload.
        _ = PublishToBrokerAsync(domainEvent);
    }

    /// <summary>
    ///     This method feeds a single event received back from the broker into the same local
    ///     channel <see cref="DnsServerEventDispatcher" /> already drains -- called only by
    ///     <see cref="Broker.BrokerEventConsumerService" />, never directly by application code.
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
