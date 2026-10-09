// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record describes one in-memory DNS answer cache on this node, as returned by
///     <c>GET /api/dns-cache</c>: the forwarded-response cache (<c>forwarded</c>), the recursive
///     resolver's cache (<c>recursive</c>) or the DNSSEC trust cache (<c>dnssec</c>, the zones
///     proven secure or insecure, with their keys). The counts are this node's only; caches are
///     not shared across a cluster.
/// </summary>
/// <param name="Name">The cache's short name, which the flush and remove actions take as <c>cache</c> to act on it alone.</param>
/// <param name="EntryCount">How many entries it holds right now, including expired ones kept for serve-stale.</param>
/// <param name="MaxEntries">The configured size bound it is trimmed back to.</param>
/// <param name="Hits">Lookups that found a fresh entry since startup or the last flush.</param>
/// <param name="Misses">Lookups that found nothing fresh over the same period.</param>
public record DnsCacheDetail(string Name, int EntryCount, int MaxEntries, long Hits, long Misses);

/// <summary>
///     This record reports what a flush (<c>DELETE /api/dns-cache</c>) or a single-name removal
///     (<c>DELETE /api/dns-cache/entries/{name}</c>) did.
/// </summary>
/// <param name="RemovedEntryCount">How many cache entries were removed, across every cache acted on.</param>
public record DnsCacheRemovalResult(int RemovedEntryCount);
