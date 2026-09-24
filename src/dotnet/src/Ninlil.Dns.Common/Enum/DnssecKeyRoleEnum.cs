using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists the two roles a DNSSEC signing key can play within a zone, per RFC
///     4034 §2.1.1's Secure Entry Point convention -- splitting "signs the zone's other records"
///     from "signs the zone's own key set" so the two can be rolled over independently and at
///     different intervals, the way real-world DNSSEC operational practice (RFC 6781) expects.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<DnssecKeyRoleEnum>>]
public enum DnssecKeyRoleEnum
{
    /// <summary>
    ///     A Zone-Signing Key: signs every ordinary RRset in the zone. Rolled over more
    ///     frequently than a <see cref="Ksk" />, since doing so needs no coordination with the
    ///     parent zone at all.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "ZSK")]
    Zsk = 0,

    /// <summary>
    ///     A Key-Signing Key: signs only the zone's own <see cref="Wire.Records.DnsKeyRecordData" />
    ///     RRset, and is the key a parent zone's <see cref="Wire.Records.DsRecordData" /> vouches
    ///     for. Rolled over far less often, since doing so requires updating that DS record in
    ///     the parent -- an out-of-band, operator-coordinated step this project does not automate.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "KSK")]
    Ksk = 1,
}
