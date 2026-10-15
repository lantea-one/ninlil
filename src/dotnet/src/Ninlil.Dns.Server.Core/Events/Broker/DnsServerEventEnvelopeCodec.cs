using JsonSerializer = Ninlil.Common.Serializer.JsonSerializer;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events.Broker;

/// <summary>
///     This class converts between an <see cref="IDnsServerEvent" /> and its
///     <see cref="DnsServerEventEnvelope" /> wire form. <see cref="BrokerEventConsumerService" />
///     and the publish path of <see cref="DnsServerEventPublisher" /> use it, so the transports
///     only move envelopes.
/// </summary>
public static class DnsServerEventEnvelopeCodec
{
    /// <summary>
    ///     This method encodes an event into an envelope stamped with the publishing instance. It
    ///     throws <see cref="InvalidOperationException" /> for an event type not in
    ///     <see cref="DnsServerEventTypeRegistry" />.
    /// </summary>
    /// <param name="domainEvent">The event to encode.</param>
    /// <param name="originInstanceId">The publishing process's instance id.</param>
    public static DnsServerEventEnvelope Encode(IDnsServerEvent domainEvent, Guid originInstanceId)
    {
        string eventTypeName = DnsServerEventTypeRegistry.GetName(domainEvent);
        string payloadJson = JsonSerializer.Serialize(domainEvent.GetType(), domainEvent);

        return new DnsServerEventEnvelope(eventTypeName, payloadJson, originInstanceId);
    }

    /// <summary>
    ///     This method decodes an envelope back into an event. It returns <see langword="false" />
    ///     for an unknown name (a newer peer) and for a type without
    ///     <see cref="DnsServerEventDeliveryFlags.CrossesBroker" />, so a peer can never inject a
    ///     settings event. A malformed payload throws.
    /// </summary>
    /// <param name="envelope">The envelope received from the broker.</param>
    /// <param name="domainEvent">The decoded event, or <see langword="null" />.</param>
    public static bool TryDecode(DnsServerEventEnvelope envelope, out IDnsServerEvent domainEvent)
    {
        domainEvent = null;

        if (
            envelope is null
            || !DnsServerEventTypeRegistry.TryGetType(envelope.EventTypeName, out Type eventType)
            || !DnsServerEventTypeRegistry
                .GetDeliveryFlags(eventType)
                .HasFlag(DnsServerEventDeliveryFlags.CrossesBroker)
        )
            return false;

        domainEvent = (IDnsServerEvent)JsonSerializer.Deserialize(eventType, envelope.PayloadJson);
        return domainEvent is not null;
    }
}
