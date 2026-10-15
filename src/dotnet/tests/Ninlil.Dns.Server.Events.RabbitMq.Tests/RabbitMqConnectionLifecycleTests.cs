using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Events.Broker;
using RabbitMQ.Client;

// Define our namespace.
namespace Ninlil.Dns.Server.Events.RabbitMq.Tests;

/// <summary>
///     This class tests the connection lifecycle of <see cref="RabbitMqDnsServerEventBrokerTransport" />
///     against a real RabbitMQ broker in a container. It closes the transport's connection from the
///     broker side with <c>rabbitmqctl close_all_connections</c> and counts the broker's open
///     connections with <c>rabbitmqctl list_connections</c>: a lost connection must be replaced by
///     exactly one new connection (never kept next to it by the client library's own recovery), and
///     a failed exchange declaration must not leave its connection open. It needs Docker.
/// </summary>
[Trait("Category", "RequiresDocker")]
public sealed class RabbitMqConnectionLifecycleTests : IAsyncLifetime
{
    /// <summary>
    ///     This constant contains the pinned broker image (tag and digest, as
    ///     <c>Ninlil.Dns.Data.EngineHosts</c> pins its images).
    /// </summary>
    private const string Image =
        "public.ecr.aws/docker/library/rabbitmq:4.1-management-alpine@sha256:5466c0d65fc84d5cd662041e71bfa3601cc30a60a709390fe0de5c4c4a51bc8c";

    private const int AmqpPort = 5672;
    private const string User = "ninlil";
    private const string Password = "ninlil-test";

    // The default guest account only accepts loopback logins inside the container, and the test
    // connects through a mapped port, so the container gets its own account.
    private readonly IContainer _container = new ContainerBuilder(Image)
        .WithPortBinding(AmqpPort, assignRandomHostPort: true)
        .WithEnvironment("RABBITMQ_DEFAULT_USER", User)
        .WithEnvironment("RABBITMQ_DEFAULT_PASS", Password)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(AmqpPort))
        .Build();

    /// <inheritdoc />
    public Task InitializeAsync() => _container.StartAsync();

    /// <inheritdoc />
    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task Reconnect_AfterThreeBrokerSideCloses_LeavesExactlyOneConnectionAndDeliversOnce()
    {
        RabbitMqEventBrokerOptions brokerOptions = NewOptions($"ninlil.test.{Guid.NewGuid():N}");

        await using RabbitMqDnsServerEventBrokerTransport transport = NewTransport(brokerOptions);

        int probes = 0;
        int markers = 0;
        using CancellationTokenSource stop = new(TimeSpan.FromMinutes(3));

        Task consumer = transport.StartConsumingAsync(
            (envelope, _) =>
            {
                if (envelope.EventTypeName == "probe")
                    Interlocked.Increment(ref probes);
                else if (envelope.EventTypeName == "marker")
                    Interlocked.Increment(ref markers);

                return Task.CompletedTask;
            },
            stop.Token
        );

        try
        {
            await WaitForAsync(() => transport.IsConnected, "The transport never connected.");
            await ProbeUntilReceivedAsync(transport, () => Volatile.Read(ref probes));

            for (int round = 1; round <= 3; round++)
            {
                HashSet<string> before = await ListConnectionsAsync();
                await ExecAsync("rabbitmqctl", "close_all_connections", "test");

                // The replacement is a connection that did not exist before the close.
                await WaitForAsync(
                    async () =>
                        transport.IsConnected
                        && (await ListConnectionsAsync()).Overlaps(before) == false
                        && (await ListConnectionsAsync()).Count > 0,
                    $"No new connection after close number {round}."
                );
            }

            // A leaked or library-recovered connection would still be open after this pause.
            await Task.Delay(TimeSpan.FromSeconds(3));
            Assert.Single(await ListConnectionsAsync());

            int probesBefore = Volatile.Read(ref probes);
            await ProbeUntilReceivedAsync(transport, () => Volatile.Read(ref probes), probesBefore);

            await transport.PublishAsync(
                new DnsServerEventEnvelope("marker", "{}", Guid.NewGuid()),
                stop.Token
            );
            await WaitForAsync(() => Volatile.Read(ref markers) > 0, "The marker never arrived.");
            await Task.Delay(1000);

            Assert.Equal(1, Volatile.Read(ref markers));
        }
        finally
        {
            await stop.CancelAsync();
            await consumer;
        }
    }

    [Fact]
    public async Task Connect_WhenTheExchangeDeclarationIsRefused_DoesNotLeaveConnectionsOpen()
    {
        string exchange = $"ninlil.test.{Guid.NewGuid():N}";

        // A direct exchange of the same name makes the transport's fanout declaration fail with
        // PRECONDITION_FAILED on every attempt.
        ConnectionFactory factory = NewFactory();
        await using (IConnection admin = await factory.CreateConnectionAsync())
        await using (IChannel channel = await admin.CreateChannelAsync())
            await channel.ExchangeDeclareAsync(exchange, ExchangeType.Direct, durable: false);

        RabbitMqEventBrokerOptions brokerOptions = NewOptions(exchange);
        brokerOptions.InitialReconnectDelay = TimeSpan.FromMilliseconds(100);
        brokerOptions.MaxReconnectDelay = TimeSpan.FromMilliseconds(200);

        await using RabbitMqDnsServerEventBrokerTransport transport = NewTransport(brokerOptions);

        // Starts the connection loop; the publish itself fails because there is no connection.
        await Assert.ThrowsAnyAsync<Exception>(() =>
            transport.PublishAsync(new DnsServerEventEnvelope("x", "{}", Guid.NewGuid()), default)
        );

        // At least three attempts (100 ms, 200 ms, 200 ms delays plus connect time).
        await Task.Delay(TimeSpan.FromSeconds(3));

        Assert.True((await ListConnectionsAsync()).Count <= 1);
        Assert.False(transport.IsConnected);
    }

    private ConnectionFactory NewFactory() =>
        new()
        {
            HostName = _container.Hostname,
            Port = _container.GetMappedPublicPort(AmqpPort),
            UserName = User,
            Password = Password,
        };

    private RabbitMqEventBrokerOptions NewOptions(string exchange) =>
        new()
        {
            HostName = _container.Hostname,
            Port = _container.GetMappedPublicPort(AmqpPort),
            UserName = User,
            Password = Password,
            ExchangeName = exchange,
            InitialReconnectDelay = TimeSpan.FromMilliseconds(200),
            MaxReconnectDelay = TimeSpan.FromSeconds(1),
        };

    private static RabbitMqDnsServerEventBrokerTransport NewTransport(
        RabbitMqEventBrokerOptions brokerOptions
    ) =>
        new(
            Options.Create(brokerOptions),
            NullLogger<RabbitMqDnsServerEventBrokerTransport>.Instance
        );

    // Publishes probe envelopes until the consumer has seen one more than it had, because the
    // consumer's queue only sees messages published after it is bound.
    private static async Task ProbeUntilReceivedAsync(
        RabbitMqDnsServerEventBrokerTransport transport,
        Func<int> probeCount,
        int baseline = 0
    )
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromMinutes(1);

        while (probeCount() <= baseline)
        {
            Assert.True(DateTime.UtcNow < deadline, "The consumer never received a probe.");

            try
            {
                await transport.PublishAsync(
                    new DnsServerEventEnvelope("probe", "{}", Guid.NewGuid()),
                    default
                );
            }
            catch (InvalidOperationException)
            {
                // Not connected yet; try again.
            }

            await Task.Delay(250);
        }
    }

    private async Task<HashSet<string>> ListConnectionsAsync()
    {
        string output = await ExecAsync("rabbitmqctl", "list_connections", "-q", "name");

        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line != "name")
            .ToHashSet();
    }

    private async Task<string> ExecAsync(params string[] command)
    {
        ExecResult result = await _container.ExecAsync(command);
        Assert.True(result.ExitCode == 0, $"{string.Join(' ', command)} failed: {result.Stderr}");

        return result.Stdout;
    }

    private static Task WaitForAsync(Func<bool> condition, string failure) =>
        WaitForAsync(() => Task.FromResult(condition()), failure);

    private static async Task WaitForAsync(Func<Task<bool>> condition, string failure)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);

        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, failure);
            await Task.Delay(200);
        }
    }
}
