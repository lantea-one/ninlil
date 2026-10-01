using Microsoft.AspNetCore.SignalR;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Web.Hubs;

/// <summary>
///     This class connects the Server.Core event bus to <see cref="DnsEventsHub" />: it
///     subscribes (<see cref="IDnsServerEventSubscriber{TEvent}" />) to each event type the hub
///     carries and forwards each event unchanged to the connected clients whose user holds the
///     permission that event type requires (see <see cref="DnsEventsHub" />). It works the same
///     whether <c>DnsServer:Events:Transport</c> is <c>InMemory</c>, <c>RabbitMq</c> or
///     <c>ActiveMq</c>, because <c>BrokerEventConsumerService</c> feeds events received from a
///     broker into the same local dispatch as in-process events.
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
    ) =>
        BroadcastAsync(
            "DnsQueryReceived",
            DnsEventsHub.QueryEventsPermission,
            domainEvent,
            stoppingToken
        );

    /// <inheritdoc />
    public ValueTask HandleAsync(
        DnsQueryAnsweredEvent domainEvent,
        CancellationToken stoppingToken
    ) =>
        BroadcastAsync(
            "DnsQueryAnswered",
            DnsEventsHub.QueryEventsPermission,
            domainEvent,
            stoppingToken
        );

    /// <inheritdoc />
    public ValueTask HandleAsync(
        DnsZoneChangedEvent domainEvent,
        CancellationToken stoppingToken
    ) =>
        BroadcastAsync(
            "DnsZoneChanged",
            DnsEventsHub.ZoneEventsPermission,
            domainEvent,
            stoppingToken
        );

    /// <inheritdoc />
    public ValueTask HandleAsync(
        DnsCacheEvictedEvent domainEvent,
        CancellationToken stoppingToken
    ) =>
        BroadcastAsync(
            "DnsCacheEvicted",
            DnsEventsHub.CacheEventsPermission,
            domainEvent,
            stoppingToken
        );

    /// <summary>
    ///     This method forwards one event unchanged, under the given client method name, to the
    ///     SignalR group of <paramref name="permission" />, never to <c>Clients.All</c>, which
    ///     would reach every signed-in user whatever their permissions.
    /// </summary>
    private async ValueTask BroadcastAsync<TEvent>(
        string methodName,
        string permission,
        TEvent domainEvent,
        CancellationToken stoppingToken
    ) =>
        await hubContext
            .Clients.Group(DnsEventsHub.GroupName(permission))
            .SendAsync(methodName, domainEvent, stoppingToken);
}
