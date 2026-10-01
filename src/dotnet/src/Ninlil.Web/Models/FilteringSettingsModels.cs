using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST shape of the filtering engine settings (<c>DnsFilterOptions</c>)
///     that <c>GET /api/filtering-settings</c> returns. Nothing in it is secret, so a
///     <c>PATCH</c> to the same endpoint accepts the same fields. The lists themselves are managed
///     through <c>/api/filter-lists</c>.
/// </summary>
/// <param name="Enable">Whether block and allow lists are applied at all. Defaults to <see langword="false" />.</param>
/// <param name="BlockResponseMode">How a blocked query is answered. Defaults to NXDOMAIN.</param>
/// <param name="SinkholeAddressV4">The address a blocked A query gets in sinkhole mode. Defaults to <c>0.0.0.0</c>.</param>
/// <param name="SinkholeAddressV6">The address a blocked AAAA query gets in sinkhole mode. Defaults to <c>::</c>.</param>
/// <param name="SinkholeTimeToLiveSeconds">The TTL, in seconds, of a synthesized sinkhole record. Defaults to 60.</param>
/// <param name="Precedence">Which list wins when a check matches both a block list and an allow list of the same kind. Defaults to allow.</param>
/// <param name="BlockCnameCloaking">Whether an answer is also blocked when one of its CNAME targets is blocked. Defaults to <see langword="true" />.</param>
public record FilteringSettingsDetail(
    bool Enable,
    DnsFilterBlockResponseModeEnum BlockResponseMode,
    string SinkholeAddressV4,
    string SinkholeAddressV6,
    uint SinkholeTimeToLiveSeconds,
    DnsFilterListPrecedenceEnum Precedence,
    bool BlockCnameCloaking
);
