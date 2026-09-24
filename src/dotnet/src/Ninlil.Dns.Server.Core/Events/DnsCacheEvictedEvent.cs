// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This event is published whenever the recursive resolver's cache evicts one or more
///     entries -- their time-to-live expiring, or the cache trimming itself under memory
///     pressure -- so a metrics subscriber can track cache health without the cache itself
///     needing to know how that health is measured or stored.
/// </summary>
/// <param name="OccurredAtUtc">The UTC date and time the eviction happened.</param>
/// <param name="Reason">A short, human-readable reason for the eviction (for example, "expired" or "capacity").</param>
/// <param name="EntryCount">How many entries were evicted.</param>
public record DnsCacheEvictedEvent(DateTimeOffset OccurredAtUtc, string Reason, int EntryCount)
    : IDnsServerEvent;
