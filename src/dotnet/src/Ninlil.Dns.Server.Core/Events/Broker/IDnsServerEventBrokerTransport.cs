// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events.Broker;

/// <summary>
///     This interface is implemented once per broker (<c>Ninlil.Dns.Server.Events.RabbitMq</c>,
///     <c>Ninlil.Dns.Server.Events.ActiveMq</c>) this project's fully-swappable event transport
///     supports. Both implementations publish to, and consume from, a single shared
///     fanout/topic destination: every connected Ninlil instance -- including the one that
///     published a given event -- receives every event, the same broadcast semantics the
///     in-memory transport already has (<see cref="DnsServerEventDispatcher" /> already invokes
///     every registered subscriber for a matching event type; a broker-backed instance must not
///     narrow that to "one instance in the fleet handles it," which would be work-queue
///     semantics instead).
/// </summary>
public interface IDnsServerEventBrokerTransport : IAsyncDisposable
{
    /// <summary>
    ///     This property reports whether this transport currently holds a live connection to
    ///     its broker -- consulted by the <c>/status</c> health-check endpoint. Both
    ///     implementations connect on a dedicated background task rather than the calling
    ///     thread (see each one's own doc comment for why), so this is the only reliable way to
    ///     observe connection state from the outside without racing that task.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    ///     This method publishes a single event to the broker. Called from
    ///     <see cref="DnsServerEventPublisher.Publish" />, which is itself deliberately
    ///     synchronous and non-blocking -- an implementation should not block the caller waiting
    ///     for broker acknowledgement on the hot query-answering path; fire-and-forget with
    ///     logged failures is the expected shape, matching the in-memory transport's own
    ///     "losing an event under extreme load is a smaller problem than blocking the pipeline"
    ///     posture.
    /// </summary>
    /// <param name="domainEvent">The event to publish.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    Task PublishAsync(IDnsServerEvent domainEvent, CancellationToken stoppingToken);

    /// <summary>
    ///     This method starts consuming events from the broker for the lifetime of the returned
    ///     task (which only ever completes when <paramref name="stoppingToken" /> is cancelled,
    ///     or the connection fails unrecoverably), invoking <paramref name="onEventReceived" />
    ///     once per received event -- including ones this same process itself published, per
    ///     this interface's own broadcast-semantics requirement.
    /// </summary>
    /// <param name="onEventReceived">The callback invoked once per received event.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    Task StartConsumingAsync(
        Func<IDnsServerEvent, CancellationToken, Task> onEventReceived,
        CancellationToken stoppingToken
    );
}
