using Microsoft.AspNetCore.SignalR;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Web.Hubs;

/// <summary>
///     This class bridges Server.Core's own internal event bus to <see cref="DnsEventsHub" />:
///     one <see cref="IDnsServerEventSubscriber{TEvent}" /> registration per curated event type,
///     each simply forwarding the event, as-is, to every currently-connected, authenticated
///     client. Works identically regardless of whether <c>DnsServer:Events:Transport</c> is
///     <c>InMemory</c>, <c>RabbitMq</c>, or <c>ActiveMq</c> -- <c>BrokerEventConsumerService</c>
///     already feeds a broker-received event back into the same local dispatch path an
///     in-process event uses, so this class needs no broker awareness of its own.
/// </summary>
/// <param name="hubContext">The hub context events are broadcast through.</param>
public sealed class DnsEventsHubBroadcaster(IHubContext<DnsEventsHub> hubContext)
    : IDnsServerEventSubscriber<DnsQueryReceivedEvent>,
        IDnsServerEventSubscriber<DnsQueryAnsweredEvent>,
        IDnsServerEventSubscriber<DnsZoneChangedEvent>,
        IDnsServerEventSubscriber<DnsCacheEvictedEvent>
{
    /// <inheritdoc />
    public ValueTask HandleAsync(
        DnsQueryReceivedEvent domainEvent,
        CancellationToken stoppingToken
    ) => BroadcastAsync("DnsQueryReceived", domainEvent, stoppingToken);

    /// <inheritdoc />
    public ValueTask HandleAsync(
        DnsQueryAnsweredEvent domainEvent,
        CancellationToken stoppingToken
    ) => BroadcastAsync("DnsQueryAnswered", domainEvent, stoppingToken);

    /// <inheritdoc />
    public ValueTask HandleAsync(
        DnsZoneChangedEvent domainEvent,
        CancellationToken stoppingToken
    ) => BroadcastAsync("DnsZoneChanged", domainEvent, stoppingToken);

    /// <inheritdoc />
    public ValueTask HandleAsync(
        DnsCacheEvictedEvent domainEvent,
        CancellationToken stoppingToken
    ) => BroadcastAsync("DnsCacheEvicted", domainEvent, stoppingToken);

    /// <summary>
    ///     This method forwards one domain event to every currently-connected, authenticated
    ///     SignalR client under the given client-side method name, with no transformation of the
    ///     event itself.
    /// </summary>
    private async ValueTask BroadcastAsync<TEvent>(
        string methodName,
        TEvent domainEvent,
        CancellationToken stoppingToken
    ) => await hubContext.Clients.All.SendAsync(methodName, domainEvent, stoppingToken);
}
