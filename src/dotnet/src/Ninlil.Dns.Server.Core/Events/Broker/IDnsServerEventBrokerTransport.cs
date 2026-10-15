// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events.Broker;

/// <summary>
///     This interface is implemented once per supported message broker
///     (<c>Ninlil.Dns.Server.Events.RabbitMq</c>, <c>Ninlil.Dns.Server.Events.ActiveMq</c>). When
///     configured, it adds an outward event stream to the in-process delivery: every connected
///     instance publishes to and consumes from one shared fanout/topic destination, so every
///     instance, including the publisher, receives every envelope. The transport only moves
///     <see cref="DnsServerEventEnvelope" />s; <see cref="DnsServerEventEnvelopeCodec" /> runs in
///     Core. Local subscribers always get this process's events directly, and
///     <see cref="BrokerEventConsumerService" /> drops an envelope the process published itself.
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
    ///     This method publishes one envelope to the broker.
    ///     <see cref="DnsServerEventPublisher.Publish" /> calls it without awaiting, so a slow
    ///     broker never holds up the query path; failures are logged by the publisher and the
    ///     event is lost to other instances.
    /// </summary>
    /// <param name="envelope">The envelope to publish.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    Task PublishAsync(DnsServerEventEnvelope envelope, CancellationToken stoppingToken);

    /// <summary>
    ///     This method consumes envelopes from the broker until <paramref name="stoppingToken" />
    ///     is cancelled or the connection fails for good, calling
    ///     <paramref name="onEnvelopeReceived" /> once per envelope, including envelopes this
    ///     process published.
    /// </summary>
    /// <param name="onEnvelopeReceived">The callback invoked once per received envelope.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    Task StartConsumingAsync(
        Func<DnsServerEventEnvelope, CancellationToken, Task> onEnvelopeReceived,
        CancellationToken stoppingToken
    );
}
