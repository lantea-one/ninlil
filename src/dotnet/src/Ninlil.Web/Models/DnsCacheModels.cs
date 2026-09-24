// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST representation of one in-memory DNS answer cache on this node:
///     the forwarded-response cache (<c>forwarded</c>) or the recursive resolver's cache
///     (<c>recursive</c>). The counts are this node's only; caches are never shared across a
///     cluster.
/// </summary>
/// <param name="Name">The cache's short name, which the flush and remove actions accept to address it alone.</param>
/// <param name="EntryCount">How many entries it holds right now, including expired ones kept for serve-stale.</param>
/// <param name="MaxEntries">The configured size bound it is trimmed back to.</param>
/// <param name="Hits">Lookups that found a fresh entry since startup or the last flush.</param>
/// <param name="Misses">Lookups that found nothing fresh over the same period.</param>
public record DnsCacheDetail(string Name, int EntryCount, int MaxEntries, long Hits, long Misses);

/// <summary>
///     This record reports what a flush or a single-name removal did.
/// </summary>
/// <param name="RemovedEntryCount">How many cache entries were removed, across every cache acted on.</param>
public record DnsCacheRemovalResult(int RemovedEntryCount);
