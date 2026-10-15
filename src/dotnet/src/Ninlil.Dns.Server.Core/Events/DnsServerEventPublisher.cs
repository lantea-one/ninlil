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
///         written to a third queue, bounded at <see cref="DnsServerEventPublisherOptions.Capacity" />
///         with the oldest event dropped when full, that
///         <see cref="BrokerEventPublishService" /> drains: it encodes each event and hands it to
///         <paramref name="brokerTransport" /> one at a time. The calling thread never encodes or
///         waits on the broker. Events received from other instances enter through
///         <see cref="EnqueueRemote" /> and reach only subscribers that implement
///         <see cref="IDnsServerRemoteEventSubscriber" />.
///     </para>
/// </summary>
/// <param name="options">The queue capacity and transport, read at startup.</param>
/// <param name="brokerTransport">
///     The broker transport to publish through when configured, or <see langword="null" /> in
///     the default in-memory-only configuration.
/// </param>
/// <param name="logger">
///     The logger this publisher records dropped events and unregistered event types to.
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
    private long _brokerDroppedSinceLastWarning;
    private long _lastBrokerDropWarningTicks;
    private Channel<QueuedDnsServerEvent> _lossyChannel;
    private Channel<IDnsServerEvent> _brokerChannel;

    /// <summary>
    ///     This property contains the bounded queue of per-query events. When full, the oldest
    ///     event is dropped and counted.
    /// </summary>
    private Channel<QueuedDnsServerEvent> LossyChannel =>
        LazyInitializer.EnsureInitialized(ref _lossyChannel, CreateLossyChannel);

    /// <summary>
    ///     This property contains the bounded queue of events waiting to be sent to the broker.
    ///     It is created on first use, so an in-memory-only process never allocates it. When
    ///     full, the oldest event is dropped and counted.
    /// </summary>
    private Channel<IDnsServerEvent> BrokerChannel =>
        LazyInitializer.EnsureInitialized(ref _brokerChannel, CreateBrokerChannel);

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

    /// <summary>
    ///     This property contains the reader of the broker publish queue that
    ///     <see cref="BrokerEventPublishService" /> drains. There is exactly one reader.
    /// </summary>
    internal ChannelReader<IDnsServerEvent> BrokerReader => BrokerChannel.Reader;

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

        // Only queued here: BrokerEventPublishService encodes and sends off this thread, so the
        // query path never waits on the broker. A failed publish is logged and not retried;
        // local subscribers already have the event.
        BrokerChannel.Writer.TryWrite(domainEvent);
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
    internal void ReportDroppedEvents() =>
        ReportDrops(
            ref _droppedSinceLastWarning,
            ref _lastDropWarningTicks,
            "The event queue was full and dropped {DroppedCount} query events; raise DnsServer:Events:Capacity or reduce subscribers' load."
        );

    /// <summary>
    ///     This method logs one warning with the number of events dropped from the broker
    ///     publish queue since the last warning, at most once per minute.
    ///     <see cref="BrokerEventPublishService" /> calls it between sends.
    /// </summary>
    internal void ReportDroppedBrokerEvents() =>
        ReportDrops(
            ref _brokerDroppedSinceLastWarning,
            ref _lastBrokerDropWarningTicks,
            "The broker publish queue was full and {DroppedCount} events were not sent to the message broker; the broker is slower than the event rate. Raise DnsServer:Events:Capacity or check the broker."
        );

    // The drop callbacks of the bounded channels run on whichever thread publishes, so the counter
    // and the last-warning time are shared without a lock. Only the single reader (the dispatcher
    // or the broker publish service) calls this method, so the check-then-reset below cannot race
    // with another reporter; a concurrent drop only lands in the next window, because Exchange
    // takes the count and zeroes it in one step. A last-warning time of 0 means "never warned".
    private void ReportDrops(ref long dropped, ref long lastWarningTicks, string message)
    {
        if (Interlocked.Read(ref dropped) == 0)
            return;

        long now = Environment.TickCount64;
        long last = Interlocked.Read(ref lastWarningTicks);

        if (last != 0 && now - last < DropWarningInterval.TotalMilliseconds)
            return;

        long count = Interlocked.Exchange(ref dropped, 0);
        // TickCount64 can in theory be 0; store 1 so the value never reads as "never warned".
        Interlocked.Exchange(ref lastWarningTicks, now == 0 ? 1 : now);

        if (count > 0)
            logger.LogWarning(message, count);
    }

    // The drop callback fires when DropOldest evicts an item, which is the only place a drop is
    // visible; ReportDroppedEvents turns the count into one warning.
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

    private Channel<IDnsServerEvent> CreateBrokerChannel() =>
        Channel.CreateBounded<IDnsServerEvent>(
            new BoundedChannelOptions(options.Value.Capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest,
            },
            _ => Interlocked.Increment(ref _brokerDroppedSinceLastWarning)
        );

    // The registry's Lossy flag decides the queue, so a per-query burst can never push out a
    // control event.
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
}

/// <summary>
///     This record is one queued event with its origin: <see cref="IsRemote" /> is
///     <see langword="true" /> for an event received from another instance through the broker.
/// </summary>
/// <param name="Event">The event.</param>
/// <param name="IsRemote">Whether another instance published the event.</param>
internal readonly record struct QueuedDnsServerEvent(IDnsServerEvent Event, bool IsRemote);
