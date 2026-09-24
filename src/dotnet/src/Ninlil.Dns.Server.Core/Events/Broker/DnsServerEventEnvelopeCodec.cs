using JsonSerializer = Ninlil.Common.Serializer.JsonSerializer;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events.Broker;

/// <summary>
///     This class converts between a live <see cref="IDnsServerEvent" /> and its own
///     <see cref="DnsServerEventEnvelope" /> wire form, shared by every
///     <see cref="IDnsServerEventBrokerTransport" /> implementation so none of them need to
///     reimplement this encoding themselves.
/// </summary>
public static class DnsServerEventEnvelopeCodec
{
    /// <summary>
    ///     This method encodes a live event into its own broker-wire envelope.
    /// </summary>
    public static DnsServerEventEnvelope Encode(IDnsServerEvent domainEvent)
    {
        string eventTypeName = DnsServerEventTypeRegistry.GetName(domainEvent);
        string payloadJson = JsonSerializer.Serialize(domainEvent.GetType(), domainEvent);

        return new DnsServerEventEnvelope(eventTypeName, payloadJson);
    }

    /// <summary>
    ///     This method decodes a broker-wire envelope back into a live event.
    /// </summary>
    public static IDnsServerEvent Decode(DnsServerEventEnvelope envelope)
    {
        Type eventType = DnsServerEventTypeRegistry.GetType(envelope.EventTypeName);

        return (IDnsServerEvent)JsonSerializer.Deserialize(eventType, envelope.PayloadJson);
    }
}
