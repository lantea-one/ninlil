// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries a single Safe Search catalog entry's own metadata, together with
///     whether it is currently enabled on this server -- the response shape for
///     <c>GET /api/safe-search</c>.
/// </summary>
/// <param name="Key">The catalog entry's own stable identifier (e.g. <c>google</c>).</param>
/// <param name="DisplayName">The human-readable engine name.</param>
/// <param name="Description">A short, honest description of what enabling this entry does.</param>
/// <param name="SourceDocumentationUrl">
///     The engine's own official documentation page this entry's rewrite mappings were read from.
/// </param>
/// <param name="HostnameCount">
///     How many hostnames this entry redirects when enabled -- a quick indicator without echoing
///     every raw mapping back.
/// </param>
/// <param name="IsEnabled">
///     Whether this server currently has every one of this entry's own rewrite mappings in place
///     as an enabled <c>DnsRewriteEntity</c> row.
/// </param>
public record SafeSearchDetail(
    string Key,
    string DisplayName,
    string Description,
    string SourceDocumentationUrl,
    int HostnameCount,
    bool IsEnabled
);
