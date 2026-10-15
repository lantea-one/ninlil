// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This interface publishes an <see cref="IDnsServerEvent" /> for later delivery to the
///     <see cref="IDnsServerEventSubscriber{TEvent}" />s registered for it. Publishing is
///     synchronous and never blocks: it writes to an in-memory queue (and starts a broker
///     publish without waiting when a broker is configured), so it is safe to call while
///     answering a query. Only per-query events can be dropped; see
///     <see cref="DnsServerEventPublisherOptions.Capacity" />.
/// </summary>
public interface IDnsServerEventPublisher
{
    /// <summary>
    ///     This method publishes an event.
    /// </summary>
    /// <param name="domainEvent">The event to publish.</param>
    void Publish(IDnsServerEvent domainEvent);
}
