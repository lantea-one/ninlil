// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST shape of the PTR sync defaults (<c>PtrSyncOptions</c>) that
///     <c>GET /api/ptr-sync-settings</c> returns: whether the A and AAAA records of a
///     <c>ZoneTypeEnum.Primary</c> or <c>ZoneTypeEnum.CloudPrimary</c> zone are mirrored as PTR
///     records into the matching reverse zone, and how reverse zones are sized. Each applies when
///     the zone's own setting is unset. A <c>PATCH</c> to the same endpoint accepts the same fields.
/// </summary>
/// <param name="DefaultEnabled">
///     Whether PTR records are managed for a zone whose <c>ZoneEntity.AutoManagePtrRecords</c> is
///     unset. Defaults to <see langword="false" />.
/// </param>
/// <param name="DefaultIpv4ReverseZonePrefixBits">
///     The IPv4 reverse zone size, in prefix bits, for a zone whose
///     <c>ZoneEntity.PtrIpv4PrefixLength</c> is unset. Defaults to 24, one zone per octet
///     boundary; 25 to 30 use RFC 2317 classless zones. Only 24 to 30 are accepted.
/// </param>
/// <param name="DefaultIpv6ReverseZonePrefixBits">
///     The IPv6 reverse zone size, in prefix bits, for a zone whose
///     <c>ZoneEntity.PtrIpv6PrefixLength</c> is unset. Defaults to 64; only multiples of 4 from 16
///     to 124 are accepted.
/// </param>
public record PtrSyncSettingsDetail(
    bool DefaultEnabled,
    int DefaultIpv4ReverseZonePrefixBits,
    int DefaultIpv6ReverseZonePrefixBits
);
