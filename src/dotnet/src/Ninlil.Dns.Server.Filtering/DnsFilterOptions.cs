using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Filtering;

/// <summary>
///     This class binds to the <c>DnsServer:Filtering</c> configuration section and controls
///     <see cref="DnsFilterEngine" /> and <see cref="DnsFilterListExternalRefreshService" />. Both
///     read it live, so <c>FilteringSettingsController</c> changes apply without a restart.
/// </summary>
public sealed class DnsFilterOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Filtering";

    /// <summary>
    ///     This property controls whether block/allow-list filtering and subscribed-list refreshes
    ///     run. It is off by default, so an operator opts in.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property controls how a domain or response-IP block-list match is answered.
    /// </summary>
    public DnsFilterBlockResponseModeEnum BlockResponseMode { get; set; } =
        DnsFilterBlockResponseModeEnum.NxDomain;

    /// <summary>
    ///     This property contains the IPv4 address a blocked A query is answered with, when
    ///     <see cref="BlockResponseMode" /> is <see cref="DnsFilterBlockResponseModeEnum.SinkholeAddress" />.
    /// </summary>
    public string SinkholeAddressV4 { get; set; } = "0.0.0.0";

    /// <summary>
    ///     This property contains the IPv6 address a blocked AAAA query is answered with, when
    ///     <see cref="BlockResponseMode" /> is <see cref="DnsFilterBlockResponseModeEnum.SinkholeAddress" />.
    /// </summary>
    public string SinkholeAddressV6 { get; set; } = "::";

    /// <summary>
    ///     This property contains the TTL, in seconds, a synthesized sinkhole record is given.
    /// </summary>
    public uint SinkholeTimeToLiveSeconds { get; set; } = 60;

    /// <summary>
    ///     This property contains which list wins when a single check matches both a block list
    ///     and an allow list of the same kind.
    /// </summary>
    public DnsFilterListPrecedenceEnum Precedence { get; set; } =
        DnsFilterListPrecedenceEnum.AllowWins;

    /// <summary>
    ///     This property controls whether an answer is also blocked when one of its CNAME targets
    ///     matches the client's domain block lists. This defeats "CNAME cloaking", where a tracker
    ///     is served from a first-party name that aliases the tracker's domain. It is on by
    ///     default: a blocked domain should stay blocked however it is reached, and the check only
    ///     runs for answers that contain a CNAME.
    /// </summary>
    public bool BlockCnameCloaking { get; set; } = true;
}
