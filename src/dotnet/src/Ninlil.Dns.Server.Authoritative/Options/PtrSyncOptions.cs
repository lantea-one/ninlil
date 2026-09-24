using Ninlil.Dns.Server.Authoritative.Ptr;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Options;

/// <summary>
///     This class binds to the <c>DnsServer:Authoritative:PtrSync</c> section of the
///     application's configuration and controls the PTR/reverse-zone sync feature's own
///     global defaults -- mirroring a <see cref="Common.Enum.ZoneTypeEnum.Primary" />/
///     <see cref="Common.Enum.ZoneTypeEnum.CloudPrimary" /> zone's own A/AAAA record set into the
///     enclosing <c>in-addr.arpa</c>/<c>ip6.arpa</c> reverse zone whenever that forward record set
///     changes. Every setting here is a *default*, overridable per zone via the matching
///     <c>ZoneEntity</c> field -- this class supplies the value only when a zone's own field is
///     <see langword="null" />.
/// </summary>
public class PtrSyncOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Authoritative:PtrSync";

    /// <summary>
    ///     This property contains the global default for whether a zone's A/AAAA record set is
    ///     automatically mirrored into a reverse zone, used whenever a zone's own
    ///     <c>ZoneEntity.AutoManagePtrRecords</c> is <see langword="null" />. Defaults
    ///     <see langword="false" /> -- deliberately different from
    ///     <c>Server.Acme.CaaAutoManagementOptions.DefaultEnabled</c>'s own on-by-default posture,
    ///     and matching this project's usual "capability-expanding toggles default off" posture
    ///     instead: unlike CAA reconciliation (which only ever
    ///     acts on a zone that already opted into ACME), enabling this feature can create a
    ///     brand-new zone (the reverse zone itself) and write records into it -- a real, visible
    ///     new capability an operator must opt into explicitly, not a pure correctness/
    ///     availability improvement in service of a choice already made.
    /// </summary>
    public bool DefaultEnabled { get; set; }

    /// <summary>
    ///     This property contains the global default IPv4 reverse-zone grouping granularity, in
    ///     bits, used whenever a zone's own <c>ZoneEntity.PtrIpv4PrefixLength</c> is
    ///     <see langword="null" />. Defaults to <c>24</c> -- the conventional octet boundary this
    ///     feature always used before RFC 2317 classless delegation support existed, so an
    ///     upgrade with no explicit configuration change behaves identically to before. A value
    ///     from <c>25</c> through <c>30</c> switches every zone that inherits this default to RFC
    ///     2317's own classless <c>"{start}/{prefix}"</c> zone-naming convention instead (see
    ///     <see cref="ReverseZoneNameCalculator" />), with the octet-aligned <c>/24</c> parent zone
    ///     automatically gaining the CNAME delegation glue RFC 2317 §4 describes. Validated at the
    ///     REST write boundary (<c>PtrSyncSettingsController.Patch</c>) to stay within
    ///     <c>24</c>-<c>30</c> inclusive -- a coarser grouping would span more than one octet
    ///     (unsupported), and <c>31</c>/<c>32</c> would leave a delegated block too small to hold a
    ///     usable host range.
    /// </summary>
    public int DefaultIpv4ReverseZonePrefixBits { get; set; } =
        ReverseZoneNameCalculator.DefaultIpv4ReverseZonePrefixBits;

    /// <summary>
    ///     This property contains the global default IPv6 reverse-zone grouping granularity, in
    ///     bits, used whenever a zone's own <c>ZoneEntity.PtrIpv6PrefixLength</c> is
    ///     <see langword="null" />. Defaults to <c>64</c> -- see
    ///     <see cref="ReverseZoneNameCalculator.DefaultIpv6ReverseZonePrefixBits" />'s own doc
    ///     comment for why that particular granularity was chosen. Validated at the REST write
    ///     boundary (<c>PtrSyncSettingsController.Patch</c>) to be a multiple of <c>4</c> (every
    ///     <c>ip6.arpa</c> label is exactly one hex nibble, per RFC 3596 -- only a multiple-of-4
    ///     bit count is even expressible as a whole number of labels) between <c>16</c> and
    ///     <c>124</c> inclusive.
    /// </summary>
    public int DefaultIpv6ReverseZonePrefixBits { get; set; } =
        ReverseZoneNameCalculator.DefaultIpv6ReverseZonePrefixBits;
}
