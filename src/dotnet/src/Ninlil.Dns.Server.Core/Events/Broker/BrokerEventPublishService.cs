using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events.Broker;

/// <summary>
///     This hosted service is the one writer to the configured
///     <see cref="IDnsServerEventBrokerTransport" />. <see cref="DnsServerEventPublisher.Publish" />
///     only queues an event; this service reads the queue, encodes each event with
///     <see cref="DnsServerEventEnvelopeCodec.Encode" /> (stamped with
///     <see cref="DnsServerEventPublisher.InstanceId" />) and awaits
///     <see cref="IDnsServerEventBrokerTransport.PublishAsync" />. Encoding and a slow broker
///     therefore never delay the thread that raised the event, and a single reader means the
///     transport's session or channel is never used by two threads at once (a JMS session and an
///     AMQP channel are not safe for concurrent publishing). With the in-memory transport it does
///     nothing. A failed publish is not retried: local subscribers already have the event. The
///     failures are logged as one warning per minute with the count and the last exception.
/// </summary>
/// <param name="publisher">The publisher whose broker queue this service drains.</param>
/// <param name="options">The configured transport; <see cref="EventTransportEnum.InMemory" /> makes this service do nothing.</param>
/// <param name="logger">The logger this service records failed publishes to.</param>
/// <param name="brokerTransport">The broker transport to publish through when configured.</param>
public sealed class BrokerEventPublishService(
    DnsServerEventPublisher publisher,
    IOptions<DnsServerEventPublisherOptions> options,
    ILogger<BrokerEventPublishService> logger,
    IDnsServerEventBrokerTransport brokerTransport = null
) : BackgroundService
{
    /// <summary>
    ///     This constant is the shortest time between two failed-publish warnings.
    /// </summary>
    private static readonly TimeSpan FailureWarningInterval = TimeSpan.FromMinutes(1);

    private long _failuresSinceLastWarning;
    private long _lastFailureWarningTicks;
    private Exception _lastFailure;
    private string _lastFailureEventType;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.Transport == EventTransportEnum.InMemory || brokerTransport is null)
            return;

        try
        {
            while (await publisher.BrokerReader.WaitToReadAsync(stoppingToken))
            {
                while (publisher.BrokerReader.TryRead(out IDnsServerEvent domainEvent))
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    await PublishOneAsync(domainEvent, stoppingToken);
                    publisher.ReportDroppedBrokerEvents();
                }

                publisher.ReportDroppedBrokerEvents();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during a graceful shutdown; events still queued are not sent.
        }
    }

    /// <summary>
    ///     This method encodes and sends one event, and records a failure instead of throwing,
    ///     so one bad event or a broker outage never ends the pump.
    /// </summary>
    private async Task PublishOneAsync(IDnsServerEvent domainEvent, CancellationToken stoppingToken)
    {
        try
        {
            DnsServerEventEnvelope envelope = DnsServerEventEnvelopeCodec.Encode(
                domainEvent,
                publisher.InstanceId
            );

            await brokerTransport.PublishAsync(envelope, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            RecordFailure(exception, domainEvent.GetType().Name);
        }
    }

    private void RecordFailure(Exception exception, string eventType)
    {
        _failuresSinceLastWarning++;
        _lastFailure = exception;
        _lastFailureEventType = eventType;

        long now = Environment.TickCount64;

        if (
            _lastFailureWarningTicks != 0
            && now - _lastFailureWarningTicks < FailureWarningInterval.TotalMilliseconds
        )
            return;

        logger.LogWarning(
            _lastFailure,
            "Failed to publish {FailureCount} events to the configured message broker since the last warning; the last was a {EventType} event.",
            _failuresSinceLastWarning,
            _lastFailureEventType
        );

        _failuresSinceLastWarning = 0;
        _lastFailure = null;
        _lastFailureWarningTicks = now == 0 ? 1 : now;
    }
}
