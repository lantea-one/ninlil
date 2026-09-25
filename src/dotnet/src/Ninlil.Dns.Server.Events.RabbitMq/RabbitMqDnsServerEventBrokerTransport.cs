using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Events.Broker;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using JsonSerializer = Ninlil.Common.Serializer.JsonSerializer;

// Define our namespace.
namespace Ninlil.Dns.Server.Events.RabbitMq;

/// <summary>
///     This class implements <see cref="IDnsServerEventBrokerTransport" /> over RabbitMQ. Every
///     connected Ninlil instance publishes to one shared fanout exchange
///     (<see cref="RabbitMqEventBrokerOptions.ExchangeName" />) and consumes from it through an
///     exclusive, auto-delete queue of its own, so every instance, including the publisher,
///     gets a copy of each event, as <see cref="IDnsServerEventBrokerTransport" /> requires.
///     <para>
///         A background loop (<see cref="MaintainConnectionAsync" />) connects and reconnects
///         with exponential backoff whenever the connection is lost, so a restarted broker is
///         picked up again without operator action. That is the only resilience: an event
///         published while disconnected is not buffered or replayed. <see cref="PublishAsync" />
///         throws at once, and <see cref="DnsServerEventPublisher" /> logs and drops the event,
///         as the in-memory transport drops events when its queue is full.
///     </para>
/// </summary>
/// <param name="options">The connection and topology settings.</param>
/// <param name="logger">The logger this transport records connection/publish failures to.</param>
public sealed class RabbitMqDnsServerEventBrokerTransport(
    IOptions<RabbitMqEventBrokerOptions> options,
    ILogger<RabbitMqDnsServerEventBrokerTransport> logger
) : IDnsServerEventBrokerTransport
{
    private readonly RabbitMqEventBrokerOptions _options = options.Value;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly Lock _connectionLock = new();

    private IConnection _connection;
    private IChannel _publishChannel;
    private Task _connectionLoopTask;

    /// <inheritdoc />
    public bool IsConnected
    {
        get
        {
            lock (_connectionLock)
                return _connection is not null;
        }
    }

    /// <summary>
    ///     This method starts the background connection loop on its first call and does nothing
    ///     on later calls. It returns at once without waiting for a connection, so a publish made
    ///     before the first connection completes fails and the event is dropped.
    /// </summary>
    private Task EnsureConnectionLoopStartedAsync()
    {
        lock (_connectionLock)
            _connectionLoopTask ??= MaintainConnectionAsync(_lifetimeCancellation.Token);

        return Task.CompletedTask;
    }

    // Connects, declares the exchange, then waits until the connection is lost or shutdown is
    // requested, and starts again. RabbitMQ.Client's automatic recovery does not cover the first
    // connection and retries at a fixed interval, so this loop does the connecting and
    // reconnecting. The delay starts at InitialReconnectDelay, doubles after each failed attempt
    // up to MaxReconnectDelay, and resets after a successful connection, so a restarting broker
    // is retried quickly and one that stays down is not hammered.
    private async Task MaintainConnectionAsync(CancellationToken stoppingToken)
    {
        TimeSpan delay = _options.InitialReconnectDelay;

        while (!stoppingToken.IsCancellationRequested)
        {
            TaskCompletionSource<bool> connectionLost = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            try
            {
                ConnectionFactory factory = new()
                {
                    HostName = _options.HostName,
                    Port = _options.Port,
                    UserName = _options.UserName,
                    Password = _options.Password,
                    VirtualHost = _options.VirtualHost,
                };

                IConnection connection = await factory.CreateConnectionAsync(stoppingToken);
                IChannel publishChannel = await connection.CreateChannelAsync(
                    cancellationToken: stoppingToken
                );

                await publishChannel.ExchangeDeclareAsync(
                    _options.ExchangeName,
                    ExchangeType.Fanout,
                    durable: true,
                    autoDelete: false,
                    cancellationToken: stoppingToken
                );

                connection.ConnectionShutdownAsync += (_, _) =>
                {
                    connectionLost.TrySetResult(true);
                    return Task.CompletedTask;
                };

                lock (_connectionLock)
                {
                    _connection = connection;
                    _publishChannel = publishChannel;
                }

                logger.LogInformation(
                    "Connected to the RabbitMQ event broker at {HostName}:{Port}.",
                    _options.HostName,
                    _options.Port
                );

                delay = _options.InitialReconnectDelay;

                // Wait until the connection is lost (ConnectionShutdownAsync above) or shutdown is
                // requested.
                await Task.WhenAny(
                    connectionLost.Task,
                    Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken)
                );
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Failed to connect to the RabbitMQ event broker at {HostName}:{Port}; retrying in {Delay}.",
                    _options.HostName,
                    _options.Port,
                    delay
                );
            }
            finally
            {
                lock (_connectionLock)
                {
                    _publishChannel = null;
                    _connection = null;
                }
            }

            if (stoppingToken.IsCancellationRequested)
                break;

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, _options.MaxReconnectDelay.Ticks));
        }
    }

    /// <inheritdoc />
    public async Task PublishAsync(IDnsServerEvent domainEvent, CancellationToken stoppingToken)
    {
        await EnsureConnectionLoopStartedAsync();

        IChannel channel =
            _publishChannel
            ?? throw new InvalidOperationException(
                "Not currently connected to the RabbitMQ event broker; the event was not published."
            );

        DnsServerEventEnvelope envelope = DnsServerEventEnvelopeCodec.Encode(domainEvent);
        byte[] body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope));

        await channel.BasicPublishAsync(
            _options.ExchangeName,
            routingKey: string.Empty,
            body,
            cancellationToken: stoppingToken
        );
    }

    /// <inheritdoc />
    public async Task StartConsumingAsync(
        Func<IDnsServerEvent, CancellationToken, Task> onEventReceived,
        CancellationToken stoppingToken
    )
    {
        await EnsureConnectionLoopStartedAsync();

        while (!stoppingToken.IsCancellationRequested)
        {
            IConnection connection;

            lock (_connectionLock)
                connection = _connection;

            if (connection is null)
            {
                try
                {
                    await Task.Delay(_options.InitialReconnectDelay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                continue;
            }

            TaskCompletionSource<bool> connectionLost = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            try
            {
                // The channel must be disposed whenever this iteration ends (cancellation, a
                // lost connection or an exception). Otherwise its exclusive queue and consumer
                // stay alive on the shared connection, and after BrokerEventConsumerService
                // restarts, the old consumer delivers every event a second time.
                await using IChannel consumeChannel = await connection.CreateChannelAsync(
                    cancellationToken: stoppingToken
                );

                await consumeChannel.ExchangeDeclareAsync(
                    _options.ExchangeName,
                    ExchangeType.Fanout,
                    durable: true,
                    autoDelete: false,
                    cancellationToken: stoppingToken
                );

                QueueDeclareOk queue = await consumeChannel.QueueDeclareAsync(
                    queue: string.Empty,
                    durable: false,
                    exclusive: true,
                    autoDelete: true,
                    cancellationToken: stoppingToken
                );

                await consumeChannel.QueueBindAsync(
                    queue.QueueName,
                    _options.ExchangeName,
                    routingKey: string.Empty,
                    cancellationToken: stoppingToken
                );

                AsyncEventingBasicConsumer consumer = new(consumeChannel);

                consumer.ReceivedAsync += async (_, eventArgs) =>
                {
                    try
                    {
                        string json = Encoding.UTF8.GetString(eventArgs.Body.Span);
                        DnsServerEventEnvelope envelope =
                            JsonSerializer.Deserialize<DnsServerEventEnvelope>(json);
                        IDnsServerEvent domainEvent = DnsServerEventEnvelopeCodec.Decode(envelope);

                        await onEventReceived(domainEvent, stoppingToken);
                    }
                    catch (Exception exception)
                    {
                        logger.LogWarning(
                            exception,
                            "Failed to process an event received from the RabbitMQ event broker."
                        );
                    }
                };

                connection.ConnectionShutdownAsync += (_, _) =>
                {
                    connectionLost.TrySetResult(true);
                    return Task.CompletedTask;
                };

                await consumeChannel.BasicConsumeAsync(
                    queue.QueueName,
                    autoAck: true,
                    consumer,
                    cancellationToken: stoppingToken
                );

                logger.LogInformation(
                    "Consuming Ninlil DNS server events from the RabbitMQ event broker via queue {QueueName}.",
                    queue.QueueName
                );

                await Task.WhenAny(
                    connectionLost.Task,
                    Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken)
                );
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "The RabbitMQ event broker consumer loop failed; it will retry once the connection recovers."
                );

                try
                {
                    await Task.Delay(_options.InitialReconnectDelay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _lifetimeCancellation.CancelAsync();

        try
        {
            if (_connectionLoopTask is not null)
                await _connectionLoopTask;
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }

        IChannel channel;
        IConnection connection;

        lock (_connectionLock)
        {
            channel = _publishChannel;
            connection = _connection;
            _publishChannel = null;
            _connection = null;
        }

        if (channel is not null)
            await channel.DisposeAsync();

        if (connection is not null)
            await connection.DisposeAsync();

        _lifetimeCancellation.Dispose();
    }
}
