// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This interface is implemented by every event on the internal event bus: a query answered,
///     a zone changed, a cache entry evicted, and so on. Code publishes through
///     <see cref="IDnsServerEventPublisher" />, and <see cref="DnsServerEventDispatcher" />
///     delivers each event to its <see cref="IDnsServerEventSubscriber{TEvent}" />s in the
///     background. The query log, webhooks, cache reloads, cluster sync and the live events hub
///     all subscribe, so none of that work runs on the query path.
/// </summary>
public interface IDnsServerEvent
{
    /// <summary>
    ///     This property contains the UTC date and time the event occurred.
    /// </summary>
    DateTimeOffset OccurredAtUtc { get; }
}
