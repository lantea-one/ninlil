// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This interface publishes a <see cref="IDnsServerEvent" /> for later, asynchronous
///     delivery to whatever <see cref="IDnsServerEventSubscriber{TEvent}" /> instances are
///     registered for it. Publishing is deliberately synchronous and non-blocking -- it only
///     ever hands the event to an in-memory queue -- so that calling it from the middle of
///     answering a query never makes that query wait on anything slower than a queue write.
/// </summary>
public interface IDnsServerEventPublisher
{
    /// <summary>
    ///     This method publishes an event.
    /// </summary>
    /// <param name="domainEvent">The event to publish.</param>
    void Publish(IDnsServerEvent domainEvent);
}
