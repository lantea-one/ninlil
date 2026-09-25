using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists the two ways a signed zone can prove a name does not exist. Both are
///     supported, not only the newer one.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<DnssecDenialOfExistenceModeEnum>>]
public enum DnssecDenialOfExistenceModeEnum
{
    /// <summary>
    ///     RFC 4034's original mechanism: a signed <see cref="Wire.Records.NsecRecordData" />
    ///     names the next owner name in the zone in the clear. It is simple, but anyone can list a
    ///     zone's contents by repeatedly asking for the "next" nonexistent name.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "NSEC")]
    Nsec = 0,

    /// <summary>
    ///     RFC 5155's hashed mechanism: a signed <see cref="Wire.Records.Nsec3RecordData" />
    ///     names the next hashed owner name instead. This stops zone walking at the cost of extra
    ///     validator CPU time per hash iteration.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "NSEC3")]
    Nsec3 = 1,
}
