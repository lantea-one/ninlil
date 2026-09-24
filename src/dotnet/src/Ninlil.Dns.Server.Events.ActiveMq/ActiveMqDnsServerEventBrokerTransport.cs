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
///     "Classic" (OpenWire): every connected Ninlil instance publishes to, and subscribes
///     to, one shared topic (<see cref="ActiveMqEventBrokerOptions.TopicName" />) -- a topic,
///     not a queue, so every instance (including the one that published a given event) receives
///     its own copy, matching the broadcast semantics
///     <see cref="IDnsServerEventBrokerTransport" />'s own doc comment requires.
///     <para>
///         The NMS client's own <c>failover:</c> transport (<see cref="BuildFailoverUri" />)
///         reconnects with exponential backoff on its own and keeps an already-established
///         connection/session/consumer/producer transparently usable across a later broker
///         restart -- confirmed by this project's own real-container smoke test. What it does
///         <em>not</em> do safely is bound the very first connection attempt: this project's own
///         smoke test found that <c>Connection.Start()</c> blocks synchronously, forever, until
///         the broker becomes reachable at least once, regardless of the <c>failover:</c> URI's
///         own <c>startupMaxReconnectAttempts</c> setting (which stops the library's internal
///         retry loop but never wakes the thread already blocked waiting on it -- a real gap in
///         the client library, not this project's own logic). The fix here is the same shape
///         <c>Server.Events.RabbitMq</c> already uses: the actual connect call runs on its own
///         dedicated background task, started once and left running for this transport's whole
///         lifetime, while <see cref="PublishAsync" /> only ever checks whether that task has
///         already succeeded -- it never itself blocks waiting for a connection, so a caller
///         gets the same immediate "drop and log" outcome
///         <see cref="Server.Core.Events.DnsServerEventPublisher" /> already expects, the
///         deliberate middle ground this project settled on.
///     </para>
/// </summary>
/// <param name="options">This transport's own configured connection and topology settings.</param>
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

    private Uri BuildFailoverUri() =>
        new(
            $"failover:(tcp://{_options.HostName}:{_options.Port})"
                + $"?initialReconnectDelay={(long)_options.InitialReconnectDelay.TotalMilliseconds}"
                + $"&maxReconnectDelay={(long)_options.MaxReconnectDelay.TotalMilliseconds}"
                + $"&timeout={(long)_options.PublishTimeout.TotalMilliseconds}"
        );

    /// <summary>
    ///     This method starts (once, idempotently) the single background task that connects to
    ///     the broker -- safe to call repeatedly from either <see cref="PublishAsync" /> or
    ///     <see cref="StartConsumingAsync" />; only the first call actually starts it.
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

            // This blocks -- potentially forever, if the broker never becomes reachable -- which
            // is exactly why Connect() only ever runs inside the dedicated background task
            // EnsureConnectTaskStarted() owns, never on a caller's own thread.
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
            // Reached only for a failure the failover transport itself does not retry (for
            // example, bad credentials) -- a genuine network-level outage is retried
            // indefinitely, with backoff, inside connection.Start() above and never reaches
            // this catch block at all.
            logger.LogError(
                exception,
                "Failed to connect to the ActiveMQ event broker at {HostName}:{Port}; this transport instance will remain unavailable until restarted.",
                _options.HostName,
                _options.Port
            );
        }
    }

    /// <inheritdoc />
    public Task PublishAsync(IDnsServerEvent domainEvent, CancellationToken stoppingToken)
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

        DnsServerEventEnvelope envelope = DnsServerEventEnvelopeCodec.Encode(domainEvent);
        byte[] body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope));

        IBytesMessage message = session.CreateBytesMessage();
        message.WriteBytes(body);

        // Once a connection has been established at least once, a later, brief interruption is
        // bounded by the failover: URI's own "timeout" parameter (PublishTimeout) rather than
        // blocking indefinitely -- matching the same "drop and log" outcome the not-yet-ever-
        // connected case above reaches by checking _publishProducer directly instead.
        producer.Send(message);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StartConsumingAsync(
        Func<IDnsServerEvent, CancellationToken, Task> onEventReceived,
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
            // Connect() already logged why -- an unrecoverable failure (not a retried network
            // outage), so this consumer simply has nothing to consume from.
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
                IDnsServerEvent domainEvent = DnsServerEventEnvelopeCodec.Decode(envelope);

                onEventReceived(domainEvent, stoppingToken).GetAwaiter().GetResult();
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
