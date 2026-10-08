using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Protocol.Plain.Tests.Fakes;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Protocol.Plain.Tests;

/// <summary>
///     This class tests which client address the plain UDP and TCP listeners deliver to the
///     resolvers when <see cref="PlainProtocolOptions.ListenAddresses" /> is empty. That setting
///     uses one dual-stack socket (bound to <c>[::]</c> with dual mode on), which reports an IPv4
///     client as an IPv4-mapped IPv6 address. The tests bind that socket for real, query it over
///     127.0.0.1 and check that a recording resolver sees plain IPv4. A host without IPv6
///     loopback cannot make such a socket, so the tests return early there (xunit 2 has no
///     runtime skip).
/// </summary>
public sealed class DualStackClientAddressTests
{
    [Fact]
    public async Task UdpClientOverIPv4_ReachesTheResolverAsPlainIPv4()
    {
        // No IPv6 on this host: the dual-stack socket cannot be created, so there is nothing to test.
        if (!LocalAddresses.CanBind(IPAddress.IPv6Loopback))
            return;

        await using Host host = await Host.StartAsync();

        using UdpClient client = new(AddressFamily.InterNetwork);
        client.Connect(IPAddress.Loopback, host.Port);
        await client.SendAsync(DnsMessageWriter.Write(BuildQuery()));

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        await client.ReceiveAsync(timeout.Token);

        IPAddress seen = Assert.Single(host.Resolver.SeenAddresses);
        Assert.Equal(AddressFamily.InterNetwork, seen.AddressFamily);
        Assert.Equal(IPAddress.Loopback, seen);
    }

    [Fact]
    public async Task TcpClientOverIPv4_ReachesTheResolverAsPlainIPv4()
    {
        // No IPv6 on this host: the dual-stack socket cannot be created, so there is nothing to test.
        if (!LocalAddresses.CanBind(IPAddress.IPv6Loopback))
            return;

        await using Host host = await Host.StartAsync();

        using TcpClient client = new(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, host.Port);

        NetworkStream stream = client.GetStream();
        byte[] message = DnsMessageWriter.Write(BuildQuery());
        byte[] prefix = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(prefix, (ushort)message.Length);
        await stream.WriteAsync(prefix);
        await stream.WriteAsync(message);

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        await stream.ReadExactlyAsync(prefix, timeout.Token);
        await stream.ReadExactlyAsync(
            new byte[BinaryPrimitives.ReadUInt16BigEndian(prefix)],
            timeout.Token
        );

        IPAddress seen = Assert.Single(host.Resolver.SeenAddresses);
        Assert.Equal(AddressFamily.InterNetwork, seen.AddressFamily);
        Assert.Equal(IPAddress.Loopback, seen);
    }

    [Fact]
    public async Task UdpClientOverIPv6_KeepsItsIPv6Address()
    {
        // No IPv6 on this host: the dual-stack socket cannot be created, so there is nothing to test.
        if (!LocalAddresses.CanBind(IPAddress.IPv6Loopback))
            return;

        await using Host host = await Host.StartAsync();

        using UdpClient client = new(AddressFamily.InterNetworkV6);
        client.Connect(IPAddress.IPv6Loopback, host.Port);
        await client.SendAsync(DnsMessageWriter.Write(BuildQuery()));

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        await client.ReceiveAsync(timeout.Token);

        Assert.Equal(IPAddress.IPv6Loopback, Assert.Single(host.Resolver.SeenAddresses));
    }

    private static DnsMessage BuildQuery() =>
        new()
        {
            Id = 99,
            Opcode = DnsOpcodeEnum.Query,
            RecursionDesired = true,
            Questions =
            {
                new DnsQuestion(
                    DnsName.Parse("dual.example."),
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet
                ),
            },
        };

    /// <summary>
    ///     This class owns one test's pipeline plus UDP and TCP listeners on the dual-stack
    ///     socket, started on a free port and stopped on dispose.
    /// </summary>
    private sealed class Host : IAsyncDisposable
    {
        private DnsQueryProcessingPipeline _pipeline;
        private UdpDnsListener _udp;
        private TcpDnsListener _tcp;

        public int Port { get; private init; }

        public RecordingResolver Resolver { get; } = new();

        public static async Task<Host> StartAsync()
        {
            Host host = new() { Port = LocalAddresses.GetFreePort() };

            host._pipeline = new DnsQueryProcessingPipeline(
                new DnsQueryDispatcher([host.Resolver], new NoOpEventPublisher()),
                new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                    new DnsQueryProcessingPipelineOptions()
                ),
                NullLogger<DnsQueryProcessingPipeline>.Instance
            );
            await host._pipeline.StartAsync(CancellationToken.None);

            StaticOptionsMonitor<PlainProtocolOptions> options = new(
                new PlainProtocolOptions { Port = host.Port, ListenAddresses = "" }
            );

            host._udp = new UdpDnsListener(
                host._pipeline,
                options,
                NullLogger<UdpDnsListener>.Instance
            );
            host._tcp = new TcpDnsListener(
                host._pipeline,
                new ServiceCollection()
                    .BuildServiceProvider()
                    .GetRequiredService<IServiceScopeFactory>(),
                options,
                NullLogger<TcpDnsListener>.Instance
            );

            await host._udp.StartAsync(CancellationToken.None);
            await host._tcp.StartAsync(CancellationToken.None);

            // BackgroundService runs ExecuteAsync (which binds) as a continuation, so give both
            // listeners a moment to bind before the test sends anything.
            await Task.Delay(200);
            return host;
        }

        public async ValueTask DisposeAsync()
        {
            await _tcp.StopAsync(CancellationToken.None);
            await _udp.StopAsync(CancellationToken.None);
            await _pipeline.StopAsync(CancellationToken.None);
            await _pipeline.DisposeAsync();
        }
    }

    /// <summary>
    ///     This resolver records the client address of every query it sees and answers with an
    ///     empty NOERROR response.
    /// </summary>
    private sealed class RecordingResolver : IDnsQueryResolver
    {
        public ConcurrentQueue<IPAddress> SeenAddresses { get; } = new();

        public ValueTask<DnsQueryResolution> TryResolveAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        )
        {
            SeenAddresses.Enqueue(context.RemoteAddress);

            return ValueTask.FromResult(
                DnsQueryResolution.Handled(
                    new DnsMessage
                    {
                        Id = context.Request.Id,
                        IsResponse = true,
                        Opcode = context.Request.Opcode,
                        ResponseCode = DnsResponseCodeEnum.NoError,
                        Questions = { context.Request.Questions[0] },
                    }
                )
            );
        }
    }

    /// <summary>This publisher discards every event.</summary>
    private sealed class NoOpEventPublisher : IDnsServerEventPublisher
    {
        public void Publish(IDnsServerEvent domainEvent) { }
    }
}
