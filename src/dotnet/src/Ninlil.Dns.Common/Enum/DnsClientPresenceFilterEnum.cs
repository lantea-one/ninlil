using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration names the presence states a list of known clients can be narrowed to. A
///     device's presence is never stored: it is online when any of its addresses was seen within
///     the configured online window, evaluated against the current instant at query time, so a
///     device that goes quiet drops to offline on its own with no background sweep.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<DnsClientPresenceFilterEnum>>]
public enum DnsClientPresenceFilterEnum
{
    /// <summary>
    ///     Only devices seen within the online window.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Online")]
    Online = 0,

    /// <summary>
    ///     Only devices not seen within the online window, including devices never seen at all.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Offline")]
    Offline = 1,
}
