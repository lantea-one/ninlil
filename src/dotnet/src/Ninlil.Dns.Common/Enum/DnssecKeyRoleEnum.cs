using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists the two roles a DNSSEC signing key can play in a zone, following the
///     Secure Entry Point convention of RFC 4034 §2.1.1. Separating "signs the zone's records" from
///     "signs the zone's key set" lets the two keys roll over independently and at different
///     intervals, as DNSSEC operational practice (RFC 6781) expects.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<DnssecKeyRoleEnum>>]
public enum DnssecKeyRoleEnum
{
    /// <summary>
    ///     A Zone-Signing Key: signs every ordinary RRset in the zone. It rolls over more often than
    ///     a <see cref="Ksk" /> because a rollover needs no coordination with the parent zone.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "ZSK")]
    Zsk = 0,

    /// <summary>
    ///     A Key-Signing Key: signs only the zone's <see cref="Wire.Records.DnsKeyRecordData" />
    ///     RRset, and is the key the parent zone's <see cref="Wire.Records.DsRecordData" /> vouches
    ///     for. It rolls over far less often because a rollover means updating that DS record in the
    ///     parent, an operator step Ninlil does not automate.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "KSK")]
    Ksk = 1,
}
