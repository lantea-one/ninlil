// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST representation of this host's own currently-effective
///     <c>DnsRewriteOptions</c>. No secret material is involved, so this record's own fields are
///     the exact same ones a <c>PATCH</c> to the same endpoint accepts. Rewrite entries themselves
///     (each individual domain/target override) are a separate resource, already fully CRUD-able
///     via <c>DnsRewritesController</c> -- this record covers only the engine-level toggle and
///     synthesized-answer TTL that applies across every configured rewrite.
/// </summary>
public record RewriteSettingsDetail(bool Enable, uint TimeToLiveSeconds);
