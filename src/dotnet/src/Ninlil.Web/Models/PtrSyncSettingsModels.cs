// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST representation of this host's own currently-effective
///     <c>PtrSyncOptions</c> -- the global default for whether a
///     <c>ZoneTypeEnum.Primary</c>/<c>ZoneTypeEnum.CloudPrimary</c> zone's own A/AAAA record set is
///     automatically mirrored into its enclosing reverse zone, used whenever a zone's own
///     <c>ZoneEntity.AutoManagePtrRecords</c> is not explicitly set. No secret material is
///     involved, so this record's own field is the exact same one a <c>PATCH</c> to the same
///     endpoint accepts.
/// </summary>
/// <param name="DefaultEnabled">
///     The global default -- overridable per zone via <c>ZoneEntity.AutoManagePtrRecords</c>,
///     which this setting supplies a value for only when that field is left unset.
/// </param>
/// <param name="DefaultIpv4ReverseZonePrefixBits">
///     The global default IPv4 reverse-zone grouping granularity, in bits -- overridable per zone
///     via <c>ZoneEntity.PtrIpv4PrefixLength</c>. <c>24</c> is the conventional octet boundary;
///     <c>25</c>-<c>30</c> groups at an RFC 2317 classless boundary instead.
/// </param>
/// <param name="DefaultIpv6ReverseZonePrefixBits">
///     The global default IPv6 reverse-zone grouping granularity, in bits -- overridable per zone
///     via <c>ZoneEntity.PtrIpv6PrefixLength</c>.
/// </param>
public record PtrSyncSettingsDetail(
    bool DefaultEnabled,
    int DefaultIpv4ReverseZonePrefixBits,
    int DefaultIpv6ReverseZonePrefixBits
);
