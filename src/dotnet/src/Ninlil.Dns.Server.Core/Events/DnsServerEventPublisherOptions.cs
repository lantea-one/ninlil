using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This class binds to the <c>DnsServer:Events</c> section of the application's
///     configuration and controls how many unpublished events <see cref="DnsServerEventPublisher" />
///     is willing to hold onto before it starts discarding the oldest ones, plus which
///     <see cref="EventTransportEnum" /> actually delivers a published event.
/// </summary>
public class DnsServerEventPublisherOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Events";

    /// <summary>
    ///     This property contains how many events may be queued for delivery at once. If
    ///     <see cref="DnsServerEventDispatcher" /> ever falls behind publishing by more than
    ///     this many events -- which would only happen under sustained, extreme query volume --
    ///     the oldest queued events are discarded to make room, rather than letting the queue
    ///     grow without bound. Losing a handful of metrics events under that kind of load is a
    ///     far smaller problem than the query-answering pipeline itself running out of memory.
    /// </summary>
    public int Capacity { get; set; } = 10_000;

    /// <summary>
    ///     This property contains which transport actually delivers a published event.
    ///     <see cref="EventTransportEnum.InMemory" /> (the default) never leaves this process;
    ///     the two broker options round-trip every event -- including this instance's own --
    ///     through the configured broker, so multiple Ninlil instances can share one event
    ///     stream (see <see cref="Broker.IDnsServerEventBrokerTransport" />'s own doc comment for
    ///     why "fully swappable," not merely additive, was the deliberate choice here).
    /// </summary>
    public EventTransportEnum Transport { get; set; } = EventTransportEnum.InMemory;
}
