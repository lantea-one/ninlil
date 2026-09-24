using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists how <c>Server.Core</c>'s own internal event bus delivers a
///     published <c>IDnsServerEvent</c> to its registered subscribers. <see cref="InMemory" /> is
///     the default: delivery never leaves the process. The two broker options make delivery
///     fully swappable rather than merely additive -- every subscriber, including ones in the
///     same process that published the event, receives it only after a real round trip through
///     the configured broker, specifically so multiple Ninlil instances can share one event
///     stream.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<EventTransportEnum>>]
public enum EventTransportEnum
{
    /// <summary>
    ///     Delivery stays entirely in-process, via an in-memory channel. This is the default
    ///     engine used when no other transport has been configured.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "InMemory")]
    InMemory = 0,

    /// <summary>
    ///     RabbitMQ, spoken over AMQP 0-9-1 via the official <c>RabbitMQ.Client</c> library.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "RabbitMq")]
    RabbitMq = 1,

    /// <summary>
    ///     Apache ActiveMQ ("Classic"), spoken over OpenWire via <c>Apache.NMS.ActiveMQ</c>.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "ActiveMq")]
    ActiveMq = 2,
}
