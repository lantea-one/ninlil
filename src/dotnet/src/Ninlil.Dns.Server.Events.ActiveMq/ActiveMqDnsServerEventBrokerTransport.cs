using System.Text;
using Apache.NMS;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Events.Broker;
using JsonSerializer = Ninlil.Common.Serializer.JsonSerializer;

// Define our namespace.
namespace Ninlil.Dns.Server.Events.ActiveMq;

/// <summary>
///     This class implements <see cref="IDnsServerEventBrokerTransport" /> over ActiveMQ
///     "Classic" (OpenWire). Every connected Ninlil instance publishes to and subscribes to one
///     shared topic (<see cref="ActiveMqEventBrokerOptions.TopicName" />). A topic, unlike a
///     queue, gives every instance, including the publisher, a copy of each event, which is the
///     broadcast behavior <see cref="IDnsServerEventBrokerTransport" /> requires.
///     <para>
///         The NMS client's <c>failover:</c> transport (<see cref="BuildFailoverUri" />)
///         reconnects with exponential backoff and keeps an established connection, session,
///         consumer and producer usable across a broker restart. It does not bound the first
///         connection attempt: <c>Connection.Start()</c> blocks until the broker has been
///         reachable once. So the connect call runs on a background task, started once and kept
///         for the transport's lifetime, the same approach as <c>Server.Events.RabbitMq</c>.
///         <see cref="PublishAsync" /> never waits for it; it only checks whether a connection
///         exists and throws if not, which <see cref="BrokerEventPublishService" />
///         logs before dropping the event.
///     </para>
///     <para>
///         <see cref="BrokerEventPublishService" /> calls <see cref="PublishAsync" /> one event at
///         a time, which the JMS rule that a session is used by one thread at a time requires for
///         the shared publish session. The send itself blocks its thread (the NMS
///         <c>IMessageProducer.Send</c>), so a slow broker delays only that service's task, never
///         the code that raised the event. <see cref="ActiveMqEventBrokerOptions.PublishTimeout" />
///         bounds one send.
///     </para>
/// </summary>
/// <param name="options">The connection and topology settings.</param>
/// <param name="logger">The logger this transport records connection/publish failures to.</param>
public sealed class ActiveMqDnsServerEventBrokerTransport(
    IOptions<ActiveMqEventBrokerOptions> options,
    ILogger<ActiveMqDnsServerEventBrokerTransport> logger
) : IDnsServerEventBrokerTransport
{
    private readonly ActiveMqEventBrokerOptions _options = options.Value;
    private readonly Lock _connectionLock = new();

    private IConnection _connection;
    private ISession _publishSession;
    private IMessageProducer _publishProducer;
    private Task _connectTask;

    /// <inheritdoc />
    public bool IsConnected
    {
        get
        {
            lock (_connectionLock)
                return _connection is not null;
        }
    }

    // The three options map to the failover transport's own parameters. "timeout" bounds a send
    // on an interrupted connection, so a broker outage cannot block the publish service forever.
    private Uri BuildFailoverUri() =>
        new(
            $"failover:(tcp://{_options.HostName}:{_options.Port})"
                + $"?initialReconnectDelay={(long)_options.InitialReconnectDelay.TotalMilliseconds}"
                + $"&maxReconnectDelay={(long)_options.MaxReconnectDelay.TotalMilliseconds}"
                + $"&timeout={(long)_options.PublishTimeout.TotalMilliseconds}"
        );

    /// <summary>
    ///     This method starts the background task that connects to the broker. Both
    ///     <see cref="PublishAsync" /> and <see cref="StartConsumingAsync" /> call it; only the
    ///     first call starts the task, and later calls return the same one.
    /// </summary>
    private Task EnsureConnectTaskStarted()
    {
        lock (_connectionLock)
            _connectTask ??= Task.Run(Connect);

        return _connectTask;
    }

    private void Connect()
    {
        try
        {
            Apache.NMS.ActiveMQ.ConnectionFactory factory = new(BuildFailoverUri());
            IConnection connection = factory.CreateConnection(_options.UserName, _options.Password);

            connection.ExceptionListener += exception =>
                logger.LogWarning(
                    exception,
                    "The ActiveMQ event broker connection reported an error; the failover transport will retry automatically."
                );

            // This blocks until the broker is reachable, possibly forever, which is why Connect()
            // runs only on the background task EnsureConnectTaskStarted() starts.
            connection.Start();

            ISession publishSession = connection.CreateSession(AcknowledgementMode.AutoAcknowledge);
            ITopic publishTopic = publishSession.GetTopic(_options.TopicName);
            IMessageProducer publishProducer = publishSession.CreateProducer(publishTopic);

            lock (_connectionLock)
            {
                _connection = connection;
                _publishSession = publishSession;
                _publishProducer = publishProducer;
            }

            logger.LogInformation(
                "Connected to the ActiveMQ event broker at {HostName}:{Port}.",
                _options.HostName,
                _options.Port
            );
        }
        catch (Exception exception)
        {
            // Reached only for a failure the failover transport does not retry, such as bad
            // credentials. A network outage is retried with backoff inside connection.Start()
            // and never gets here.
            logger.LogError(
                exception,
                "Failed to connect to the ActiveMQ event broker at {HostName}:{Port}; this transport instance will remain unavailable until restarted.",
                _options.HostName,
                _options.Port
            );
        }
    }

    /// <inheritdoc />
    public Task PublishAsync(DnsServerEventEnvelope envelope, CancellationToken stoppingToken)
    {
        _ = EnsureConnectTaskStarted();

        IMessageProducer producer;
        ISession session;

        lock (_connectionLock)
        {
            producer = _publishProducer;
            session = _publishSession;
        }

        if (producer is null)
            throw new InvalidOperationException(
                "Not currently connected to the ActiveMQ event broker; the event was not published."
            );

        byte[] body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope));

        IBytesMessage message = session.CreateBytesMessage();
        message.WriteBytes(body);

        // The caller (BrokerEventPublishService) never overlaps two sends, so this session is
        // used by one thread at a time. Once connected, a send during a later interruption waits
        // at most the failover: URI's "timeout" parameter (PublishTimeout) and then throws, so
        // the event is dropped and logged, as in the never-connected case above. The ActiveMQ
        // client's producer (Apache.NMS.ActiveMQ 2.2.0) has no SendAsync, so the blocking Send
        // stays.
        producer.Send(message);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StartConsumingAsync(
        Func<DnsServerEventEnvelope, CancellationToken, Task> onEnvelopeReceived,
        CancellationToken stoppingToken
    )
    {
        try
        {
            await EnsureConnectTaskStarted().WaitAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        IConnection connection;

        lock (_connectionLock)
            connection = _connection;

        if (connection is null)
        {
            // Connect() failed in a way the failover transport does not retry and has logged
            // why, so there is nothing to consume from.
            return;
        }

        ISession consumeSession = connection.CreateSession(AcknowledgementMode.AutoAcknowledge);
        ITopic topic = consumeSession.GetTopic(_options.TopicName);
        IMessageConsumer consumer = consumeSession.CreateConsumer(topic);

        consumer.Listener += message =>
        {
            try
            {
                if (message is not IBytesMessage bytesMessage)
                    return;

                byte[] body = new byte[bytesMessage.BodyLength];
                bytesMessage.ReadBytes(body);

                string json = Encoding.UTF8.GetString(body);
                DnsServerEventEnvelope envelope =
                    JsonSerializer.Deserialize<DnsServerEventEnvelope>(json);

                onEnvelopeReceived(envelope, stoppingToken).GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Failed to process an event received from the ActiveMQ event broker."
                );
            }
        };

        logger.LogInformation(
            "Consuming Ninlil DNS server events from the ActiveMQ event broker via topic {TopicName}.",
            _options.TopicName
        );

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected during a graceful shutdown.
        }
        finally
        {
            consumer.Close();
            consumeSession.Close();
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        lock (_connectionLock)
        {
            _publishProducer?.Close();
            _publishSession?.Close();
            _connection?.Close();
            _connection?.Dispose();

            _publishProducer = null;
            _publishSession = null;
            _connection = null;
        }

        return ValueTask.CompletedTask;
    }
}
