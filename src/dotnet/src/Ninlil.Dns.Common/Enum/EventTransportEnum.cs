using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists how the <c>Server.Core</c> event bus delivers a published
///     <c>IDnsServerEvent</c> to its subscribers. <see cref="InMemory" /> is the default and never
///     leaves the process. A broker option replaces in-process delivery instead of adding to it:
///     every subscriber, including those in the publishing process, receives the event only after
///     a round trip through the broker, so several Ninlil instances can share one event stream.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<EventTransportEnum>>]
public enum EventTransportEnum
{
    /// <summary>
    ///     Delivery stays in-process, through an in-memory channel. This is the default when no
    ///     other transport is configured.
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
