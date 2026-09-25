using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This class binds to the <c>DnsServer:Events</c> section of the application's
///     configuration and controls how many undelivered events <see cref="DnsServerEventPublisher" />
///     holds before dropping the oldest, and which <see cref="EventTransportEnum" /> carries them.
///     Both are read at startup.
/// </summary>
public class DnsServerEventPublisherOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Events";

    /// <summary>
    ///     This property contains how many events may wait for delivery. When
    ///     <see cref="DnsServerEventDispatcher" /> falls this far behind, the oldest events are
    ///     dropped silently rather than letting memory grow. Every event type shares the queue, so
    ///     under heavy query load a dropped event may be a zone or settings change, not just a
    ///     query event.
    /// </summary>
    public int Capacity { get; set; } = 10_000;

    /// <summary>
    ///     This property contains which transport carries published events.
    ///     <see cref="EventTransportEnum.InMemory" /> (the default) stays in this process. The
    ///     broker options send every event, including this instance's, through the broker, so
    ///     several instances share one event stream (see
    ///     <see cref="Broker.IDnsServerEventBrokerTransport" />).
    /// </summary>
    public EventTransportEnum Transport { get; set; } = EventTransportEnum.InMemory;
}
