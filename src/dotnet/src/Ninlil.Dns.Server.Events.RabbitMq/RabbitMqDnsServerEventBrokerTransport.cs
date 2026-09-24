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
///     This class implements <see cref="IDnsServerEventBrokerTransport" /> over RabbitMQ: every
///     connected Ninlil instance publishes to, and consumes from, one shared fanout exchange
///     (<see cref="RabbitMqEventBrokerOptions.ExchangeName" />) via its own exclusive,
///     auto-delete queue, giving every instance -- including the one that published a given
///     event -- a copy of it, matching the broadcast semantics
///     <see cref="IDnsServerEventBrokerTransport" />'s own doc comment requires.
///     <para>
///         Connection management runs its own background loop
///         (<see cref="MaintainConnectionAsync" />) that reconnects automatically, with
///         exponential backoff, whenever the broker connection is lost -- so a restarted broker
///         is picked back up with no operator action. This is deliberately the only resilience
///         this class offers: an event published while disconnected is not buffered or
///         replayed, it simply fails <see cref="PublishAsync" /> immediately (caught and logged
///         by <see cref="DnsServerEventPublisher" />, the same "drop and log" posture the
///         in-memory transport already has under overload) -- the deliberate middle ground
///         between "some failover" and "not overly complex" this project settled on.
///     </para>
/// </summary>
/// <param name="options">This transport's own configured connection and topology settings.</param>
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
    ///     This method starts (once) the background connection-maintenance loop and blocks
    ///     until the first connection attempt has either succeeded or failed, so a caller's own
    ///     first publish/consume attempt is not racing an initial, never-yet-started connection.
    /// </summary>
    private Task EnsureConnectionLoopStartedAsync()
    {
        lock (_connectionLock)
            _connectionLoopTask ??= MaintainConnectionAsync(_lifetimeCancellation.Token);

        return Task.CompletedTask;
    }

    // What: this is the whole lifetime of the connection -- connect, publish the topology,
    // then sit idle until that connection is lost or shutdown is requested, at which point the
    // loop goes right back to the top and tries again. Why: RabbitMQ.Client's own connection
    // object gives no built-in reconnect-with-backoff behavior the way the sibling ActiveMQ
    // transport's failover: URI does, so this transport has to drive its own retry loop by hand.
    // How: delay starts at InitialReconnectDelay and doubles after every failed attempt, capped
    // at MaxReconnectDelay, then resets back to InitialReconnectDelay as soon as a connection
    // actually succeeds -- classic exponential backoff, so a broker that is merely restarting is
    // retried quickly while one that is genuinely down for longer is not hammered indefinitely.
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

                // Wait here, doing nothing else, until this connection is lost or we're asked
                // to stop -- ConnectionShutdownAsync (above) is what wakes this back up.
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
                // Disposed via this using declaration whenever this iteration ends for any
                // reason -- cancellation (a restart of the owning BrokerEventConsumerService),
                // a lost connection, or an unexpected exception -- unlike the sibling ActiveMQ
                // transport's own equivalent finally block, this channel was previously never
                // closed on a clean stop; RabbitMQ kept its exclusive queue and consumer
                // registration alive on the still-open shared connection, so a restart left the
                // old, orphaned consumer still delivering every event a second time (once per
                // still-open channel) straight into the very same onEventReceived callback a
                // freshly started consumer also now delivers to.
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
