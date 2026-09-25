// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events.Broker;

/// <summary>
///     This interface is implemented once per supported message broker
///     (<c>Ninlil.Dns.Server.Events.RabbitMq</c>, <c>Ninlil.Dns.Server.Events.ActiveMq</c>). When
///     configured, it replaces the in-memory transport. Both publish to and consume from one
///     shared fanout/topic destination, so every connected instance, including the publisher,
///     receives every event. That keeps the broadcast behavior of the in-memory transport; a
///     work queue, where one instance handles each event, would not.
/// </summary>
public interface IDnsServerEventBrokerTransport : IAsyncDisposable
{
    /// <summary>
    ///     This property reports whether the transport has a live connection to its broker. The
    ///     <c>/status</c> health check reads it. Both implementations connect on a background
    ///     task, so this is how outside code sees the connection state.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    ///     This method publishes one event to the broker.
    ///     <see cref="DnsServerEventPublisher.Publish" /> calls it without awaiting, so a slow
    ///     broker never holds up the query path; failures are logged by the publisher and the
    ///     event is lost.
    /// </summary>
    /// <param name="domainEvent">The event to publish.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    Task PublishAsync(IDnsServerEvent domainEvent, CancellationToken stoppingToken);

    /// <summary>
    ///     This method consumes events from the broker until <paramref name="stoppingToken" /> is
    ///     cancelled or the connection fails for good, calling <paramref name="onEventReceived" />
    ///     once per event, including events this process published.
    /// </summary>
    /// <param name="onEventReceived">The callback invoked once per received event.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    Task StartConsumingAsync(
        Func<IDnsServerEvent, CancellationToken, Task> onEventReceived,
        CancellationToken stoppingToken
    );
}
