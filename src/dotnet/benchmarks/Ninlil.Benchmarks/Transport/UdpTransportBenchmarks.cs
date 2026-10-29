using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Benchmarks.Pipeline;
using Ninlil.Benchmarks.Storage;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Protocol.Plain;

// Define our namespace.
namespace Ninlil.Benchmarks.Transport;

/// <summary>
///     This class measures a DNS query over UDP through a real <see cref="UdpDnsListener" /> on
///     <c>127.0.0.1</c> and an OS-assigned port, with the full pipeline of
///     <see cref="PipelineBenchmarkHost" /> behind it (every engine on, data set at the
///     <c>--scale</c>). Each client is a connected UDP socket that sends the query for an
///     authoritative A record and waits for the reply, 6,400 round trips per invocation split
///     evenly over <see cref="Clients" /> clients that run at the same time, so the mean is the
///     time per answered query and the allocation column the memory per query, client side
///     included. One client measures the latency of a round trip; 64 clients measure what the
///     listener's single receive loop per socket can carry. The setup checks that each
///     client's reply is the expected answer and carries its own query ID.
/// </summary>
[Config(typeof(StorageConfig))]
public class UdpTransportBenchmarks
{
    private const int RoundTrips = 6400;

    private PipelineBenchmarkHost _host;
    private UdpDnsListener _listener;
    private Socket[] _sockets;
    private byte[][] _queries;
    private byte[][] _buffers;

    /// <summary>
    ///     This property contains the number of clients that query at the same time;
    ///     BenchmarkDotNet sets it.
    /// </summary>
    [Params(1, 64)]
    public int Clients { get; set; }

    /// <summary>
    ///     This method starts the host and the listener, connects the clients and checks one
    ///     round trip per client.
    /// </summary>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        _host = await PipelineBenchmarkHost.StartAsync(
            PipelineBenchmarkHost.FeatureSetEnum.Full,
            BenchmarkEnvironment.Scale
        );
        await _host.WarmUpAndCheckAsync();

        // A port number is free for both UDP and TCP when a UDP socket was just bound to it.
        int port = FreeUdpPort();
        ServiceCollection options = new();
        options.Configure<PlainProtocolOptions>(o =>
        {
            o.Port = port;
            o.ListenAddresses = "127.0.0.1";
        });
        _listener = new UdpDnsListener(
            _host.Pipeline,
            options
                .BuildServiceProvider()
                .GetRequiredService<IOptionsMonitor<PlainProtocolOptions>>(),
            _host.Services.GetRequiredService<ILogger<UdpDnsListener>>()
        );
        await _listener.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => _listener.IsListening);

        _sockets = new Socket[Clients];
        _queries = new byte[Clients][];
        _buffers = new byte[Clients][];
        DnsName name = _host.NameFor(PipelineBenchmarkHost.ScenarioEnum.AuthoritativeHit);

        for (int client = 0; client < Clients; client++)
        {
            _sockets[client] = new Socket(
                AddressFamily.InterNetwork,
                SocketType.Dgram,
                ProtocolType.Udp
            );
            await _sockets[client].ConnectAsync(new IPEndPoint(IPAddress.Loopback, port));
            _queries[client] = PipelineBenchmarkHost.BuildQuery(name, id: (ushort)(client + 1));
            _buffers[client] = new byte[4096];

            int reply = await RoundTripAsync(client);
            if (reply != PipelineBenchmarkHost.Pack(DnsResponseCodeEnum.NoError, 1))
                throw new InvalidOperationException(
                    $"Benchmark setup check failed: client {client} got reply {reply:X} over UDP."
                );
        }
    }

    /// <summary>
    ///     This method closes the clients, stops the listener and the host.
    /// </summary>
    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        foreach (Socket socket in _sockets)
            socket.Dispose();

        await _listener.StopAsync(CancellationToken.None);
        _listener.Dispose();
        await _host.DisposeAsync();
    }

    /// <summary>
    ///     This method runs 6,400 round trips, split over the clients that run at the same
    ///     time.
    /// </summary>
    [Benchmark(OperationsPerInvoke = RoundTrips)]
    public async Task<int> RoundTrips_AllClientsAtOnce()
    {
        int each = RoundTrips / Clients;
        Task<int>[] clients = new Task<int>[Clients];

        for (int client = 0; client < Clients; client++)
            clients[client] = RunClientAsync(client, each);

        return (await Task.WhenAll(clients)).Sum();
    }

    private async Task<int> RunClientAsync(int client, int rounds)
    {
        // Sockets run their continuations on the thread pool, so a client that starts here
        // never blocks the others.
        await Task.Yield();
        int last = 0;

        for (int round = 0; round < rounds; round++)
            last = await RoundTripAsync(client);

        return last;
    }

    private async Task<int> RoundTripAsync(int client)
    {
        await _sockets[client].SendAsync(_queries[client]);
        int received = await _sockets[client].ReceiveAsync(_buffers[client]);

        // Header: ID in bytes 0-1, response code in the low nibble of byte 3, ANCOUNT in 6-7.
        ReadOnlySpan<byte> reply = _buffers[client].AsSpan(0, received);

        if (BinaryPrimitives.ReadUInt16BigEndian(reply) != client + 1)
            throw new InvalidOperationException("A UDP reply carried another client's ID.");

        return ((reply[3] & 0x0F) << 16) | BinaryPrimitives.ReadUInt16BigEndian(reply[6..]);
    }

    private static int FreeUdpPort()
    {
        using Socket probe = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.LocalEndPoint).Port;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        // BackgroundService binds its sockets on a continuation after StartAsync returns.
        for (int attempt = 0; attempt < 500 && !condition(); attempt++)
            await Task.Delay(10);

        if (!condition())
            throw new InvalidOperationException("The listener did not start.");
    }
}
