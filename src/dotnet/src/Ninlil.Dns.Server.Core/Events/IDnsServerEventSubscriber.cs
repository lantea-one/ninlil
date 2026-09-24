// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This interface is implemented once per event type a piece of code wants to react to --
///     for example, an <c>IDnsServerEventSubscriber&lt;DnsQueryAnsweredEvent&gt;</c> that writes
///     query metrics to the database. Any number of subscribers can be registered for the same
///     event type; <see cref="DnsServerEventDispatcher" /> invokes every one of them whenever a
///     matching event is published.
/// </summary>
/// <typeparam name="TEvent">The type of event this subscriber reacts to.</typeparam>
public interface IDnsServerEventSubscriber<in TEvent>
    where TEvent : IDnsServerEvent
{
    /// <summary>
    ///     This method handles a single published event.
    /// </summary>
    /// <param name="domainEvent">The event to handle.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    ValueTask HandleAsync(TEvent domainEvent, CancellationToken stoppingToken);
}
