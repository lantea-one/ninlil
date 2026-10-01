// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST shape of the rewrite engine settings (<c>DnsRewriteOptions</c>)
///     that <c>GET /api/rewrite-settings</c> returns. A <c>PATCH</c> to the same endpoint accepts
///     the same fields. The rewrites themselves are a separate resource
///     (<c>DnsRewritesController</c>); this record holds the on/off switch and the TTL of
///     synthesized answers, which apply to all of them.
/// </summary>
/// <param name="Enable">Whether rewrites are applied. Defaults to <see langword="false" />.</param>
/// <param name="TimeToLiveSeconds">The TTL, in seconds, of a synthesized rewrite answer. Defaults to 300.</param>
public record RewriteSettingsDetail(bool Enable, uint TimeToLiveSeconds);
