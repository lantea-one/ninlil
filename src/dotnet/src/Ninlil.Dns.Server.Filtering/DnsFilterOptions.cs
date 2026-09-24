using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Filtering;

/// <summary>
///     This class binds to the <c>DnsServer:Filtering</c> section of the application's
///     configuration and controls <see cref="DnsFilterEngine" />'s and
///     <see cref="DnsFilterListExternalRefreshService" />'s own behavior.
/// </summary>
public sealed class DnsFilterOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Filtering";

    /// <summary>
    ///     This property controls whether block/allow-list filtering runs at all. Off by
    ///     default, the same reasoning as every other transport's own <c>Enable</c> toggle -- a
    ///     new capability an operator should opt into.
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
}
