// Define our namespace.
namespace Ninlil.Dns.Server.Core.Caching;

/// <summary>
///     This interface is the one thing <see cref="DnsCacheStore{TKey, TEntry}" /> needs to know
///     about whatever it holds: when the entry stops being fresh. Everything else about an entry
///     (an RRset, a whole forwarded response, a negative answer) is the owning cache's business.
/// </summary>
public interface IDnsCacheEntry
{
    /// <summary>
    ///     This property contains the point this entry stops being valid to serve as a fresh
    ///     answer. It may still be served stale after this, if the owning cache allows it.
    /// </summary>
    DateTimeOffset ExpiresAtUtc { get; }
}
