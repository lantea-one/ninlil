// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries one Safe Search catalog entry with whether it is enabled, the response
///     shape of <c>GET /api/safe-search</c>.
/// </summary>
/// <param name="Key">The catalog entry's stable identifier (e.g. <c>google</c>).</param>
/// <param name="DisplayName">The human-readable search engine name.</param>
/// <param name="Description">A short description of what enabling the entry does.</param>
/// <param name="SourceDocumentationUrl">
///     The engine's official documentation page the entry's rewrite mappings come from.
/// </param>
/// <param name="HostnameCount">
///     How many hostnames the entry redirects when enabled, without listing every mapping.
/// </param>
/// <param name="IsEnabled">
///     Whether every one of the entry's mappings exists as an enabled <c>DnsRewriteEntity</c>
///     with the matching scope (global, or the client or client group the request named).
/// </param>
public record SafeSearchDetail(
    string Key,
    string DisplayName,
    string Description,
    string SourceDocumentationUrl,
    int HostnameCount,
    bool IsEnabled
);
