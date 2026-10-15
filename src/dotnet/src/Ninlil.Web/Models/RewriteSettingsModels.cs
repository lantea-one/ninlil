// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST shape of the rewrite engine settings (<c>DnsRewriteOptions</c>)
///     that <c>GET /api/rewrite-settings</c> returns. A <c>PATCH</c> to the same endpoint accepts
///     the same fields. The rewrites themselves are a separate resource
///     (<c>DnsRewritesController</c>); this record holds the on/off switch and the TTL of
///     synthesized answers, which apply to all of them. Both fields are replicated to every
///     cluster node, so the record also reports which node last wrote them.
/// </summary>
/// <param name="Enable">Whether rewrites are applied. Defaults to <see langword="false" />.</param>
/// <param name="TimeToLiveSeconds">The TTL, in seconds, of a synthesized rewrite answer. Defaults to 300.</param>
/// <param name="LastModifiedUtc">When the settings were last written, or <see langword="null" /> if never.</param>
/// <param name="OriginatingNodeId">The cluster node the last write came from, or <see langword="null" /> if never written.</param>
public record RewriteSettingsDetail(
    bool Enable,
    uint TimeToLiveSeconds,
    DateTimeOffset? LastModifiedUtc = null,
    Guid? OriginatingNodeId = null
);
