using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration describes how the forwarding resolver picks among several configured
///     upstream forwarders (<c>ForwardingOptions.FailoverStrategy</c>). It is Ninlil vocabulary,
///     not an IANA-assigned value.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<ForwarderFailoverStrategyEnum>>]
public enum ForwarderFailoverStrategyEnum
{
    /// <summary>
    ///     Forwarders are tried in their configured order. A later one is used only when every
    ///     earlier one fails or times out for that query. There is no background health checking.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "TryInOrder")]
    TryInOrder = 0,

    /// <summary>
    ///     Every forwarder is health-checked in the background, and queries round-robin across the
    ///     healthy ones. This is the default. It spreads load and survives failures better than
    ///     <see cref="TryInOrder" />, at the cost of a background health-check loop.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "HealthCheckedRoundRobin")]
    HealthCheckedRoundRobin = 1,
}
