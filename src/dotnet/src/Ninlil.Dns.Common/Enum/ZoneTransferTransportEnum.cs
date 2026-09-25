using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration describes which transport a zone's AXFR/IXFR traffic uses. For a
///     <see cref="ZoneTypeEnum.Secondary" />, <see cref="ZoneTypeEnum.Stub" /> or
///     <see cref="ZoneTypeEnum.SecondaryCatalog" /> zone it is the transport
///     <c>SecondaryZoneRefreshService</c> pulls over. For a <see cref="ZoneTypeEnum.Primary" /> or
///     <see cref="ZoneTypeEnum.CloudPrimary" /> zone it is the listener that answers inbound
///     transfer requests. RFC 5936 (AXFR) and RFC 1995 (IXFR) do not name a transport; this is
///     Ninlil vocabulary for the transport-encryption option of RFC 9103.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<ZoneTransferTransportEnum>>]
public enum ZoneTransferTransportEnum
{
    /// <summary>
    ///     Plain, unencrypted DNS over TCP, as RFC 5936 and RFC 1995 assume. This is the default.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Plain")]
    Plain = 0,

    /// <summary>
    ///     RFC 9103 XFR-over-TLS: the same AXFR/IXFR exchange inside a TLS session, so a network
    ///     observer cannot see the zone content. TSIG (RFC 8945) still does the authentication; TLS
    ///     only adds confidentiality.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Tls")]
    Tls = 1,

    /// <summary>
    ///     The same confidentiality as <see cref="Tls" />, carried over QUIC (RFC 9250) instead of
    ///     TCP and TLS. RFC 9250 only defines stub-resolver queries, not zone transfer, so this is a
    ///     Ninlil extension of that transport to server-to-server AXFR/IXFR.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Quic")]
    Quic = 2,
}
