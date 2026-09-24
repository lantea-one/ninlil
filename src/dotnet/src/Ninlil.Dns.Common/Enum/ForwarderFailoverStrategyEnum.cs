using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration describes how the forwarding resolver picks among multiple
///     configured upstream forwarders -- Ninlil's own operational vocabulary, not an
///     IANA-assigned value.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<ForwarderFailoverStrategyEnum>>]
public enum ForwarderFailoverStrategyEnum
{
    /// <summary>
    ///     Forwarders are tried strictly in their configured order; a later one is only ever
    ///     used if every earlier one fails or times out for the query at hand. No active
    ///     background health-checking.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "TryInOrder")]
    TryInOrder = 0,

    /// <summary>
    ///     Every configured forwarder is health-checked periodically in the background; queries
    ///     round-robin across whichever forwarders are currently healthy -- this project's own
    ///     default, more resilient and better load-distributed than <see cref="TryInOrder" /> at
    ///     the cost of a background health-check loop.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "HealthCheckedRoundRobin")]
    HealthCheckedRoundRobin = 1,
}
