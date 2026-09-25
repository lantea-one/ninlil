// Define our namespace.
namespace Ninlil.Dns.Server.Core.Caching;

/// <summary>
///     This interface is what <see cref="DnsCacheStore{TKey, TEntry}" /> needs to know about an
///     entry: when it stops being fresh. The rest of the entry (an RRset, a forwarded response, a
///     negative answer) belongs to the cache that owns it.
/// </summary>
public interface IDnsCacheEntry
{
    /// <summary>
    ///     This property contains when this entry stops being fresh. The owning cache may still
    ///     serve it stale after this.
    /// </summary>
    DateTimeOffset ExpiresAtUtc { get; }
}
