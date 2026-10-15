// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events.Broker;

/// <summary>
///     This enumeration says how the event bus treats one event type. <see cref="DnsServerEventTypeRegistry" />
///     holds the flags per type; <see cref="DnsServerEventPublisher" /> reads them to pick a queue
///     and to decide whether the event also goes to the message broker, and
///     <see cref="DnsServerEventEnvelopeCodec" /> refuses to decode a type without
///     <see cref="CrossesBroker" />.
/// </summary>
[Flags]
public enum DnsServerEventDeliveryFlags
{
    /// <summary>
    ///     The event stays in this process and is never dropped from its queue.
    /// </summary>
    None = 0,

    /// <summary>
    ///     The event is produced per query. When the bounded queue is full, the oldest such event
    ///     is dropped and counted.
    /// </summary>
    Lossy = 1,

    /// <summary>
    ///     The event is published to the message broker as well as delivered locally, and an
    ///     envelope of this type received from the broker is accepted.
    /// </summary>
    CrossesBroker = 2,
}
