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
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Protocol.Plain;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.RateLimiting;

// Define our namespace.
namespace Ninlil.Benchmarks.Transport;

/// <summary>
///     This class measures a DNS query over TCP through a real <see cref="TcpDnsListener" /> on
///     <c>127.0.0.1</c> and an OS-assigned port, with the full pipeline of
///     <see cref="PipelineBenchmarkHost" /> behind it (every engine on, data set at the
///     <c>--scale</c>). Two scenarios: 64 queries sent back to back on one connection, each
///     with its own query ID, then all 64 framed replies read (RFC 1035 two-octet length
///     framing); and one query on a new connection that is closed afterwards. The mean is per
///     query. The new-connection client closes with a zero linger time so the benchmark does not
///     fill the local port range with connections in TIME_WAIT. The setup checks that the
///     pipelined replies carry every query ID once and are all the expected answer.
/// </summary>
[Config(typeof(StorageConfig))]
public class TcpTransportBenchmarks
{
    private const int Pipelined = 64;

    private PipelineBenchmarkHost _host;
    private TcpDnsListener _listener;
    private int _port;
    private byte[] _pipelinedRequest;
    private byte[] _singleRequest;
    private Socket _connection;
    private byte[] _buffer;

    /// <summary>
    ///     This method starts the host and the listener, opens the long-lived connection and
    ///     checks one pipelined batch.
    /// </summary>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        _host = await PipelineBenchmarkHost.StartAsync(
            PipelineBenchmarkHost.FeatureSetEnum.Full,
            BenchmarkEnvironment.Scale
        );
        await _host.WarmUpAndCheckAsync();

        using (Socket probe = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
        {
            probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            _port = ((IPEndPoint)probe.LocalEndPoint).Port;
        }

        ServiceCollection options = new();
        options.Configure<PlainProtocolOptions>(o =>
        {
            o.Port = _port;
            o.ListenAddresses = "127.0.0.1";
        });
        _listener = new TcpDnsListener(
            _host.Pipeline,
            _host.Services.GetRequiredService<IServiceScopeFactory>(),
            options
                .BuildServiceProvider()
                .GetRequiredService<IOptionsMonitor<PlainProtocolOptions>>(),
            _host.Services.GetRequiredService<ILogger<TcpDnsListener>>(),
            _host.Services.GetService<IDnsRateLimiter>(),
            _host.Services.GetService<IDnsServerEventPublisher>()
        );
        await _listener.StartAsync(CancellationToken.None);

        // BackgroundService binds its sockets on a continuation after StartAsync returns.
        for (int attempt = 0; attempt < 500 && !_listener.IsListening; attempt++)
            await Task.Delay(10);

        if (!_listener.IsListening)
            throw new InvalidOperationException("The listener did not start.");

        DnsName name = _host.NameFor(PipelineBenchmarkHost.ScenarioEnum.AuthoritativeHit);
        _singleRequest = Frame(PipelineBenchmarkHost.BuildQuery(name, id: 1));
        _pipelinedRequest = [];

        for (ushort id = 1; id <= Pipelined; id++)
            _pipelinedRequest =
            [
                .. _pipelinedRequest,
                .. Frame(PipelineBenchmarkHost.BuildQuery(name, id: id)),
            ];

        _buffer = new byte[4096];
        _connection = await ConnectAsync();
        HashSet<int> ids = await RoundTripBatchAsync();

        if (ids.Count != Pipelined)
            throw new InvalidOperationException(
                $"Benchmark setup check failed: {ids.Count} distinct query IDs came back of {Pipelined}."
            );
    }

    /// <summary>
    ///     This method closes the connection, stops the listener and the host.
    /// </summary>
    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        _connection.Dispose();
        await _listener.StopAsync(CancellationToken.None);
        await _host.DisposeAsync();
    }

    /// <summary>
    ///     This method sends 64 queries back to back on the open connection and reads the 64
    ///     replies.
    /// </summary>
    [Benchmark(OperationsPerInvoke = Pipelined)]
    public async Task<int> Pipelined64OnOneConnection() => (await RoundTripBatchAsync()).Count;

    /// <summary>
    ///     This method opens a connection, sends one query, reads the reply and closes the
    ///     connection.
    /// </summary>
    [Benchmark]
    public async Task<int> OneQueryPerNewConnection()
    {
        using Socket socket = await ConnectAsync();
        socket.LingerState = new LingerOption(true, 0);
        await socket.SendAsync(_singleRequest);
        await ReadFrameAsync(socket);
        return 1;
    }

    private async Task<Socket> ConnectAsync()
    {
        Socket socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true,
        };
        await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, _port));
        return socket;
    }

    private async Task<HashSet<int>> RoundTripBatchAsync()
    {
        await _connection.SendAsync(_pipelinedRequest);
        HashSet<int> ids = [];

        for (int reply = 0; reply < Pipelined; reply++)
            ids.Add(await ReadFrameAsync(_connection));

        return ids;
    }

    /// <summary>
    ///     This method reads one length-prefixed reply and returns its query ID; it throws when
    ///     the reply is not a NOERROR answer with one record.
    /// </summary>
    private async Task<int> ReadFrameAsync(Socket socket)
    {
        await ReadExactlyAsync(socket, 2);
        int length = BinaryPrimitives.ReadUInt16BigEndian(_buffer);
        byte[] frame = new byte[length];
        int read = 0;

        while (read < length)
            read += await socket.ReceiveAsync(frame.AsMemory(read));

        if ((frame[3] & 0x0F) != 0 || BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(6)) != 1)
            throw new InvalidOperationException("A TCP reply was not the expected answer.");

        return BinaryPrimitives.ReadUInt16BigEndian(frame);
    }

    private async Task ReadExactlyAsync(Socket socket, int count)
    {
        int read = 0;

        while (read < count)
        {
            int received = await socket.ReceiveAsync(_buffer.AsMemory(read, count - read));

            if (received == 0)
                throw new InvalidOperationException("The server closed the connection.");

            read += received;
        }
    }

    private static byte[] Frame(byte[] message)
    {
        byte[] framed = new byte[message.Length + 2];
        BinaryPrimitives.WriteUInt16BigEndian(framed, (ushort)message.Length);
        message.CopyTo(framed, 2);
        return framed;
    }
}
