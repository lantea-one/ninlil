using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This class binds to the <c>DnsServer:Events</c> section of the application's
///     configuration and controls how many undelivered per-query events
///     <see cref="DnsServerEventPublisher" /> holds before dropping the oldest, and which
///     <see cref="EventTransportEnum" /> adds an outward stream. Both are read at startup.
/// </summary>
public class DnsServerEventPublisherOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Events";

    /// <summary>
    ///     This property contains how many per-query events (<c>DnsQueryReceived</c> and
    ///     <c>DnsQueryAnswered</c>) may wait for delivery, and how many may wait for the broker
    ///     publisher. When <see cref="DnsServerEventDispatcher" /> falls this far behind, the
    ///     oldest per-query events are dropped rather than letting memory grow, and one warning
    ///     per minute reports how many. Every other event type is never dropped.
    /// </summary>
    public int Capacity { get; set; } = 10_000;

    /// <summary>
    ///     This property contains which transport carries published events.
    ///     <see cref="EventTransportEnum.InMemory" /> (the default) stays in this process. A
    ///     broker option adds an outward stream: this instance's events go to the broker as well
    ///     as to its own subscribers, and events from other instances reach only the admin
    ///     console's live feed (see <see cref="Broker.IDnsServerEventBrokerTransport" />).
    /// </summary>
    public EventTransportEnum Transport { get; set; } = EventTransportEnum.InMemory;
}
