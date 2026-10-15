using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.Events.Broker;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This class implements <see cref="IDnsServerEventPublisher" /> by writing to in-memory
///     channels that <see cref="DnsServerEventDispatcher" /> drains. One singleton is registered
///     under this type and the interface, so the dispatcher can reach the channels while other
///     code uses the interface.
///     <para>
///         Local delivery never depends on the transport or on registration: <see cref="Publish" />
///         always enqueues the event locally. A per-query type
///         (<see cref="DnsServerEventDeliveryFlags.Lossy" />) goes to a bounded queue of
///         <see cref="DnsServerEventPublisherOptions.Capacity" /> that drops the oldest event
///         when full; every other type goes to an unbounded control queue, so a zone, rule,
///         lease, settings or user change is never dropped. Control events come from operator
///         actions, DHCP grants and background jobs at rates the dispatcher clears, and a lost
///         control event would leave a cache stale until restart.
///     </para>
///     <para>
///         When <see cref="DnsServerEventPublisherOptions.Transport" /> names a broker and the
///         type has <see cref="DnsServerEventDeliveryFlags.CrossesBroker" />, the event is also
///         sent to <paramref name="brokerTransport" /> without waiting. Events received from other
///         instances enter through <see cref="EnqueueRemote" /> and reach only subscribers that
///         implement <see cref="IDnsServerRemoteEventSubscriber" />.
///     </para>
/// </summary>
/// <param name="options">The queue capacity and transport, read at startup.</param>
/// <param name="brokerTransport">
///     The broker transport to publish through when configured, or <see langword="null" /> in
///     the default in-memory-only configuration.
/// </param>
/// <param name="logger">
///     The logger this publisher records a failed broker publish, dropped events and unregistered
///     event types to.
/// </param>
public sealed class DnsServerEventPublisher(
    IOptions<DnsServerEventPublisherOptions> options,
    ILogger<DnsServerEventPublisher> logger,
    IDnsServerEventBrokerTransport brokerTransport = null
) : IDnsServerEventPublisher
{
    /// <summary>
    ///     This constant is the shortest time between two dropped-event warnings.
    /// </summary>
    private static readonly TimeSpan DropWarningInterval = TimeSpan.FromMinutes(1);

    private readonly Channel<QueuedDnsServerEvent> _controlChannel =
        Channel.CreateUnbounded<QueuedDnsServerEvent>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false }
        );

    private readonly ConcurrentDictionary<Type, byte> _warnedUnregisteredTypes = new();
    private long _droppedSinceLastWarning;
    private long _lastDropWarningTicks;
    private Channel<QueuedDnsServerEvent> _lossyChannel;

    /// <summary>
    ///     This property contains the bounded queue of per-query events. When full, the oldest
    ///     event is dropped and counted.
    /// </summary>
    private Channel<QueuedDnsServerEvent> LossyChannel =>
        LazyInitializer.EnsureInitialized(ref _lossyChannel, CreateLossyChannel);

    /// <summary>
    ///     This property contains the per-process id stamped on every envelope this instance
    ///     publishes, so <see cref="Broker.BrokerEventConsumerService" /> can recognise its own
    ///     events when the broker echoes them back. It is not configured.
    /// </summary>
    public Guid InstanceId { get; } = Guid.NewGuid();

    /// <summary>
    ///     This property contains the reader of the control (never dropped) queue
    ///     <see cref="DnsServerEventDispatcher" /> drains first.
    /// </summary>
    internal ChannelReader<QueuedDnsServerEvent> ControlReader => _controlChannel.Reader;

    /// <summary>
    ///     This property contains the reader of the per-query (droppable) queue
    ///     <see cref="DnsServerEventDispatcher" /> drains when the control queue is empty.
    /// </summary>
    internal ChannelReader<QueuedDnsServerEvent> LossyReader => LossyChannel.Reader;

    /// <inheritdoc />
    public void Publish(IDnsServerEvent domainEvent)
    {
        Enqueue(new QueuedDnsServerEvent(domainEvent, false));

        if (options.Value.Transport == EventTransportEnum.InMemory || brokerTransport is null)
            return;

        if (!DnsServerEventTypeRegistry.TryGetName(domainEvent.GetType(), out _))
        {
            WarnOnceAboutUnregisteredType(domainEvent.GetType());
            return;
        }

        if (
            !DnsServerEventTypeRegistry
                .GetDeliveryFlags(domainEvent.GetType())
                .HasFlag(DnsServerEventDeliveryFlags.CrossesBroker)
        )
            return;

        // Not awaited, so the query path never waits on the broker. A failed publish is logged
        // and not retried; local subscribers already have the event.
        _ = PublishToBrokerAsync(domainEvent);
    }

    /// <summary>
    ///     This method puts an event received from the broker into the local queues, flagged as
    ///     remote. Only <see cref="Broker.BrokerEventConsumerService" /> calls it.
    /// </summary>
    internal void EnqueueRemote(IDnsServerEvent domainEvent) =>
        Enqueue(new QueuedDnsServerEvent(domainEvent, true));

    /// <summary>
    ///     This method logs one warning with the number of per-query events dropped since the
    ///     last warning, at most once per minute, and does nothing when none were dropped.
    ///     <see cref="DnsServerEventDispatcher" /> calls it between events, never the publishing
    ///     thread, and drops are never logged one by one.
    /// </summary>
    internal void ReportDroppedEvents()
    {
        if (Interlocked.Read(ref _droppedSinceLastWarning) == 0)
            return;

        long now = Environment.TickCount64;
        long last = Interlocked.Read(ref _lastDropWarningTicks);

        if (last != 0 && now - last < DropWarningInterval.TotalMilliseconds)
            return;

        long dropped = Interlocked.Exchange(ref _droppedSinceLastWarning, 0);
        Interlocked.Exchange(ref _lastDropWarningTicks, now == 0 ? 1 : now);

        if (dropped > 0)
            logger.LogWarning(
                "The event queue was full and dropped {DroppedCount} query events; raise DnsServer:Events:Capacity or reduce subscribers' load.",
                dropped
            );
    }

    private Channel<QueuedDnsServerEvent> CreateLossyChannel() =>
        Channel.CreateBounded<QueuedDnsServerEvent>(
            new BoundedChannelOptions(options.Value.Capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest,
            },
            _ => Interlocked.Increment(ref _droppedSinceLastWarning)
        );

    private void Enqueue(QueuedDnsServerEvent item)
    {
        Type eventType = item.Event.GetType();

        if (
            DnsServerEventTypeRegistry
                .GetDeliveryFlags(eventType)
                .HasFlag(DnsServerEventDeliveryFlags.Lossy)
        )
            LossyChannel.Writer.TryWrite(item);
        else
            _controlChannel.Writer.TryWrite(item);
    }

    private void WarnOnceAboutUnregisteredType(Type eventType)
    {
        if (_warnedUnregisteredTypes.TryAdd(eventType, 0))
            logger.LogWarning(
                "Event type {EventType} is not registered in DnsServerEventTypeRegistry; it is delivered locally and not sent to the message broker.",
                eventType.Name
            );
    }

    private async Task PublishToBrokerAsync(IDnsServerEvent domainEvent)
    {
        try
        {
            DnsServerEventEnvelope envelope = DnsServerEventEnvelopeCodec.Encode(
                domainEvent,
                InstanceId
            );

            await brokerTransport.PublishAsync(envelope, CancellationToken.None);
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

/// <summary>
///     This record is one queued event with its origin: <see cref="IsRemote" /> is
///     <see langword="true" /> for an event received from another instance through the broker.
/// </summary>
/// <param name="Event">The event.</param>
/// <param name="IsRemote">Whether another instance published the event.</param>
internal readonly record struct QueuedDnsServerEvent(IDnsServerEvent Event, bool IsRemote);
