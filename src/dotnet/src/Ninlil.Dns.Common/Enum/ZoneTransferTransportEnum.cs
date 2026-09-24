using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration describes which wire transport a zone's own AXFR/IXFR traffic uses --
///     both the outbound pull <c>SecondaryZoneRefreshService</c> performs when this server hosts
///     the zone as a <see cref="ZoneTypeEnum.Secondary" />/<see cref="ZoneTypeEnum.Stub" />/
///     <see cref="ZoneTypeEnum.SecondaryCatalog" /> copy, and which listener actually answers an
///     inbound transfer request when this server hosts the zone as a
///     <see cref="ZoneTypeEnum.Primary" />/<see cref="ZoneTypeEnum.CloudPrimary" />. RFC 5936
///     (AXFR)/RFC 1995 (IXFR) themselves say nothing about which transport carries them -- this is
///     Ninlil's own operational vocabulary for RFC 9103's transport-encryption option, the same way
///     <see cref="ZoneTypeEnum" /> is its own vocabulary for a zone's role.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<ZoneTransferTransportEnum>>]
public enum ZoneTransferTransportEnum
{
    /// <summary>
    ///     Plain DNS-over-TCP, per RFC 5936/1995's own original, unencrypted assumption. The
    ///     default -- every zone created before this feature existed keeps behaving exactly as it
    ///     always did.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Plain")]
    Plain = 0,

    /// <summary>
    ///     RFC 9103 XFR-over-TLS: the same AXFR/IXFR exchange, carried inside a TLS session so a
    ///     network observer cannot see the transferred zone content. TSIG (RFC 8945) remains the
    ///     actual authentication layer either way -- this only adds transport confidentiality.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Tls")]
    Tls = 1,

    /// <summary>
    ///     The same confidentiality benefit as <see cref="Tls" />, carried over QUIC (RFC 9250)
    ///     instead of a TCP-then-TLS handshake -- RFC 9250 itself only defines ordinary
    ///     stub-resolver queries, not zone transfer, so this is Ninlil's own analogous extension of
    ///     that transport to a server-to-server AXFR/IXFR exchange.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Quic")]
    Quic = 2,
}
