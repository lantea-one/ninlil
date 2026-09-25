// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This event is published when a DNS answer cache (<c>DnsCacheStore</c>, used by the
///     recursive and forwarded caches) removes entries: expired or over the size bound, flushed,
///     or removed by name. The live events hub and webhooks subscribe to it.
/// </summary>
/// <param name="OccurredAtUtc">The UTC date and time the eviction happened.</param>
/// <param name="Reason">
///     A short reason, such as <c>expired-or-capacity</c> for a sweep, or the cache name plus
///     <c>-flushed</c> or <c>-removed</c>.
/// </param>
/// <param name="EntryCount">How many entries were evicted.</param>
public record DnsCacheEvictedEvent(DateTimeOffset OccurredAtUtc, string Reason, int EntryCount)
    : IDnsServerEvent;
