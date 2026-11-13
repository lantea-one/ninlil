using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
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

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
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

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
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
    public async Task TcpClientOverIPv4_WhenAnotherSocketSharesThePort_UsesAnotherPort()
    {
        // No IPv6 on this host: the dual-stack socket cannot be created, so there is nothing to test.
        if (!LocalAddresses.CanBind(IPAddress.IPv6Loopback))
            return;

        // On the first port, a plain IPv4 listener takes 127.0.0.1 after the dual-stack listeners
        // are up. Windows allows that bind and sends IPv4 clients to it; Linux refuses it, and the
        // host then has no intruder to move away from.
        List<Socket> intruders = [];

        try
        {
            await using Host host = await Host.StartAsync(
                (port, attempt) =>
                {
                    if (attempt != 1)
                        return;

                    Socket intruder = new(
                        AddressFamily.InterNetwork,
                        SocketType.Stream,
                        ProtocolType.Tcp
                    );

                    try
                    {
                        intruder.Bind(new IPEndPoint(IPAddress.Loopback, port));
                        intruder.Listen();
                        intruders.Add(intruder);
                    }
                    catch (SocketException)
                    {
                        intruder.Dispose();
                    }
                }
            );

            Assert.DoesNotContain(
                intruders,
                intruder => host.Port == ((IPEndPoint)intruder.LocalEndPoint).Port
            );

            using TcpClient client = new(AddressFamily.InterNetwork);
            await client.ConnectAsync(IPAddress.Loopback, host.Port);

            NetworkStream stream = client.GetStream();
            byte[] message = DnsMessageWriter.Write(BuildQuery());
            byte[] prefix = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(prefix, (ushort)message.Length);
            await stream.WriteAsync(prefix);
            await stream.WriteAsync(message);

            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
            await stream.ReadExactlyAsync(prefix, timeout.Token);

            Assert.Equal(IPAddress.Loopback, Assert.Single(host.Resolver.SeenAddresses));
        }
        finally
        {
            foreach (Socket intruder in intruders)
                intruder.Dispose();
        }
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

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
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

        public int Port { get; private set; }

        public RecordingResolver Resolver { get; } = new();

        /// <summary>
        ///     This method starts the pipeline and both listeners on one port that the OS picks,
        ///     and returns once they listen.
        /// </summary>
        /// <param name="afterListening">
        ///     Called with the port and the attempt number once both listeners are up and before
        ///     the port is checked; a test uses it to put another socket on the port.
        /// </param>
        public static async Task<Host> StartAsync(Action<int, int> afterListening = null)
        {
            Host host = new();

            host._pipeline = new DnsQueryProcessingPipeline(
                new DnsQueryDispatcher([host.Resolver], new NoOpEventPublisher()),
                new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                    new DnsQueryProcessingPipelineOptions()
                ),
                NullLogger<DnsQueryProcessingPipeline>.Instance
            );
            await host._pipeline.StartAsync(CancellationToken.None);

            IServiceScopeFactory scopes = new ServiceCollection()
                .BuildServiceProvider()
                .GetRequiredService<IServiceScopeFactory>();

            // UDP and TCP must share one port number. UDP binds port 0 and the OS picks it; TCP
            // then asks for that number, which another process may have taken for TCP in the
            // meantime, or which Windows excludes for TCP only (a run of such ports can be
            // hundreds long, and the OS hands UDP port-0 binds out in sequence). A port that
            // another socket also holds (see HoldsOnlyListener) is as unusable. A failed pair
            // moves on to the next port, up to LocalAddresses.PortSearchLimit times.
            for (int attempt = 1; ; attempt++)
            {
                host._udp = new UdpDnsListener(
                    host._pipeline,
                    new StaticOptionsMonitor<PlainProtocolOptions>(
                        new PlainProtocolOptions { Port = 0, ListenAddresses = "" }
                    ),
                    NullLogger<UdpDnsListener>.Instance
                );
                await host._udp.StartAsync(CancellationToken.None);
                await WaitForBindOutcomeAsync(() =>
                    host._udp.IsListening || host._udp.FailedListenAddresses.Count > 0
                );

                Assert.True(host._udp.IsListening);
                host.Port = host._udp.BoundPort;

                host._tcp = new TcpDnsListener(
                    host._pipeline,
                    scopes,
                    new StaticOptionsMonitor<PlainProtocolOptions>(
                        new PlainProtocolOptions { Port = host.Port, ListenAddresses = "" }
                    ),
                    NullLogger<TcpDnsListener>.Instance
                );
                await host._tcp.StartAsync(CancellationToken.None);
                await WaitForBindOutcomeAsync(() =>
                    host._tcp.IsListening || host._tcp.FailedListenAddresses.Count > 0
                );

                if (host._tcp.IsListening)
                {
                    afterListening?.Invoke(host.Port, attempt);

                    if (HoldsOnlyListener(host.Port))
                        return host;
                }

                await host._tcp.StopAsync(CancellationToken.None);
                await host._udp.StopAsync(CancellationToken.None);

                if (attempt == LocalAddresses.PortSearchLimit)
                    throw new InvalidOperationException("No port was free for both UDP and TCP.");
            }
        }

        /// <summary>
        ///     This method reports whether the dual-stack sockets are the only TCP listener and
        ///     the only UDP socket on a port. Windows lets a socket bound to one specific address
        ///     (127.0.0.1) share a port with a wildcard socket and sends clients of that address
        ///     to the specific one, so an IPv4 client of a port that another program also holds
        ///     reaches that program and never the listener under test. A bind probe cannot tell
        ///     (a shared bind succeeds, and an exclusive one fails against the listener itself),
        ///     so the check reads the operating system's socket list. Windows lists one
        ///     dual-stack socket as two wildcard entries (0.0.0.0 and ::), Linux as one (::), and
        ///     a socket on a specific address, or a second wildcard socket, as an entry of its
        ///     own. The port is clean when every entry is a wildcard and no address appears twice.
        ///     Another socket can still appear between this check and the client's connect; that
        ///     takes a program that binds exactly this port within milliseconds, which is why the
        ///     check is not repeated.
        /// </summary>
        private static bool HoldsOnlyListener(int port)
        {
            IPGlobalProperties properties = IPGlobalProperties.GetIPGlobalProperties();

            return IsOnlyWildcards(properties.GetActiveTcpListeners(), port)
                && IsOnlyWildcards(properties.GetActiveUdpListeners(), port);
        }

        /// <summary>
        ///     This method reports whether the entries on a port are wildcard addresses, each
        ///     listed once. See <see cref="HoldsOnlyListener" />.
        /// </summary>
        private static bool IsOnlyWildcards(IPEndPoint[] endpoints, int port)
        {
            List<IPAddress> onPort = endpoints
                .Where(endpoint => endpoint.Port == port)
                .Select(endpoint => endpoint.Address)
                .ToList();

            return onPort.All(address =>
                    address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)
                )
                && onPort.Distinct().Count() == onPort.Count;
        }

        /// <summary>
        ///     This method waits until the listener reports listening or a failed bind. The
        ///     listeners bind in ExecuteAsync, which may run after StartAsync returns, and a fixed
        ///     delay is too short on a loaded machine.
        /// </summary>
        private static async Task WaitForBindOutcomeAsync(Func<bool> hasOutcome)
        {
            using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(30));

            while (!hasOutcome())
            {
                Assert.False(
                    deadline.IsCancellationRequested,
                    "The listener did not report a bind outcome in time."
                );
                await Task.Delay(10);
            }
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
