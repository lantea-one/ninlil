// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This interface is implemented once per event type a class reacts to, for example an
///     <c>IDnsServerEventSubscriber&lt;DnsQueryAnsweredEvent&gt;</c> that writes the query log.
///     Any number of subscribers can be registered for one event type;
///     <see cref="DnsServerEventDispatcher" /> calls them one after another, in a new DI scope
///     per event.
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
