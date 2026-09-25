using JsonSerializer = Ninlil.Common.Serializer.JsonSerializer;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events.Broker;

/// <summary>
///     This class converts between an <see cref="IDnsServerEvent" /> and its
///     <see cref="DnsServerEventEnvelope" /> wire form. Every
///     <see cref="IDnsServerEventBrokerTransport" /> uses it.
/// </summary>
public static class DnsServerEventEnvelopeCodec
{
    /// <summary>
    ///     This method encodes an event into an envelope. It throws
    ///     <see cref="InvalidOperationException" /> for an event type not in
    ///     <see cref="DnsServerEventTypeRegistry" />.
    /// </summary>
    public static DnsServerEventEnvelope Encode(IDnsServerEvent domainEvent)
    {
        string eventTypeName = DnsServerEventTypeRegistry.GetName(domainEvent);
        string payloadJson = JsonSerializer.Serialize(domainEvent.GetType(), domainEvent);

        return new DnsServerEventEnvelope(eventTypeName, payloadJson);
    }

    /// <summary>
    ///     This method decodes an envelope back into an event.
    /// </summary>
    public static IDnsServerEvent Decode(DnsServerEventEnvelope envelope)
    {
        Type eventType = DnsServerEventTypeRegistry.GetType(envelope.EventTypeName);

        return (IDnsServerEvent)JsonSerializer.Deserialize(eventType, envelope.PayloadJson);
    }
}
