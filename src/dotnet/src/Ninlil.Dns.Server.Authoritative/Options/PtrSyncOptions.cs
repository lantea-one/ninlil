using Ninlil.Dns.Server.Authoritative.Ptr;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Options;

/// <summary>
///     This class binds to the <c>DnsServer:Authoritative:PtrSync</c> section of the
///     application's configuration and holds the global defaults for PTR sync, which mirrors a
///     <see cref="Common.Enum.ZoneTypeEnum.Primary" /> or
///     <see cref="Common.Enum.ZoneTypeEnum.CloudPrimary" /> zone's A/AAAA records into the matching
///     <c>in-addr.arpa</c> or <c>ip6.arpa</c> reverse zone (<see cref="Ptr.PtrSyncService" />).
///     Each value applies only when the zone's matching <c>ZoneEntity</c> field is
///     <see langword="null" />.
/// </summary>
public class PtrSyncOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Authoritative:PtrSync";

    /// <summary>
    ///     This property contains the default for whether a zone's A/AAAA records are mirrored
    ///     into a reverse zone, used when <c>ZoneEntity.AutoManagePtrRecords</c> is
    ///     <see langword="null" />. Off by default, unlike CAA management, because it can create a
    ///     new reverse zone and write records into it, which an operator should opt into.
    /// </summary>
    public bool DefaultEnabled { get; set; }

    /// <summary>
    ///     This property contains the default IPv4 reverse zone size, in prefix bits, used when
    ///     <c>ZoneEntity.PtrIpv4PrefixLength</c> is <see langword="null" />. The default, 24, gives
    ///     one zone per octet boundary. Values 25 to 30 use RFC 2317 classless zones named
    ///     <c>"{start}/{prefix}"</c> (see <see cref="ReverseZoneNameCalculator" />), and the
    ///     enclosing /24 zone gets the CNAME records RFC 2317 §4 describes.
    ///     <c>PtrSyncSettingsController.Patch</c> accepts only 24 to 30: shorter prefixes would
    ///     span more than one octet, and /31 or /32 leave too few hosts.
    /// </summary>
    public int DefaultIpv4ReverseZonePrefixBits { get; set; } =
        ReverseZoneNameCalculator.DefaultIpv4ReverseZonePrefixBits;

    /// <summary>
    ///     This property contains the default IPv6 reverse zone size, in prefix bits, used when
    ///     <c>ZoneEntity.PtrIpv6PrefixLength</c> is <see langword="null" />. The default is 64 (see
    ///     <see cref="ReverseZoneNameCalculator.DefaultIpv6ReverseZonePrefixBits" />).
    ///     <c>PtrSyncSettingsController.Patch</c> accepts multiples of 4 from 16 to 124, since each
    ///     <c>ip6.arpa</c> label is one hex nibble (RFC 3596).
    /// </summary>
    public int DefaultIpv6ReverseZonePrefixBits { get; set; } =
        ReverseZoneNameCalculator.DefaultIpv6ReverseZonePrefixBits;
}
