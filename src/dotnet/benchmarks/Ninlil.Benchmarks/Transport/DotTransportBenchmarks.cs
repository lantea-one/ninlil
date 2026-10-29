using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Benchmarks.Pipeline;
using Ninlil.Benchmarks.Storage;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Certificates;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.RateLimiting;

// Define our namespace.
namespace Ninlil.Benchmarks.Transport;

/// <summary>
///     This class measures a DNS query over DNS-over-TLS (RFC 7858) through a real
///     <see cref="Ninlil.Dns.Protocol.DnsOverTls.DnsOverTlsListener" /> on <c>127.0.0.1</c> and an
///     OS-assigned port, with the full pipeline of <see cref="PipelineBenchmarkHost" /> behind it
///     (every engine on, data set at the <c>--scale</c>). The listener uses a self-signed
///     certificate stored in a temporary directory that the cleanup deletes. The client is a plain
///     <see cref="SslStream" /> that accepts that certificate: the repository's
///     <c>TlsForwarderClient</c> validates against the operating system's trust store and opens a
///     new connection per query, so it can neither trust the certificate nor pipeline. Two
///     scenarios, as in <see cref="TcpTransportBenchmarks" />: 64 queries sent back to back on one
///     connection, each with its own query ID, then all 64 framed replies read; and one query on a
///     new connection (TCP connect, TLS handshake, query, close). The mean is per query. The setup
///     checks that the pipelined replies carry every query ID once and are all the expected
///     answer.
/// </summary>
[Config(typeof(StorageConfig))]
public class DotTransportBenchmarks
{
    private const int Pipelined = 64;

    private PipelineBenchmarkHost _host;
    private Ninlil.Dns.Protocol.DnsOverTls.DnsOverTlsListener _listener;
    private string _stateDirectory;
    private int _port;
    private byte[] _pipelinedRequest;
    private byte[] _singleRequest;
    private SslStream _connection;
    private byte[] _buffer;

    /// <summary>
    ///     This method starts the host and the listener, opens the long-lived connection and
    ///     checks one pipelined batch.
    /// </summary>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        try
        {
            await StartAsync();
        }
        catch
        {
            // A failed setup never reaches the cleanup method, so the state directory goes here.
            DeleteStateDirectory();
            throw;
        }
    }

    /// <summary>
    ///     This method does the work of <see cref="SetupAsync" />; the caller removes the state
    ///     directory if it throws.
    /// </summary>
    private async Task StartAsync()
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

        _stateDirectory = Path.Combine(Path.GetTempPath(), $"ninlil-bench-dot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_stateDirectory);

        Ninlil.Dns.Protocol.DnsOverTls.DnsOverTlsOptions dotOptions = new()
        {
            Enable = true,
            Port = _port,
            ListenAddresses = "127.0.0.1",
            Certificate = new TlsCertificateOptions
            {
                SelfSignedCertificateStatePath = Path.Combine(_stateDirectory, "dot.pfx"),
                SelfSignedSubjectAlternativeNames = ["localhost"],
            },
        };
        _listener = new Ninlil.Dns.Protocol.DnsOverTls.DnsOverTlsListener(
            _host.Pipeline,
            _host.Services.GetRequiredService<IServiceScopeFactory>(),
            new FixedOptionsMonitor<Ninlil.Dns.Protocol.DnsOverTls.DnsOverTlsOptions>(dotOptions),
            _host.Services.GetRequiredService<
                ILogger<Ninlil.Dns.Protocol.DnsOverTls.DnsOverTlsListener>
            >(),
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
    ///     This method closes the connection, stops the listener and the host and deletes the
    ///     certificate directory.
    /// </summary>
    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        try
        {
            await _connection.DisposeAsync();
            await _listener.StopAsync(CancellationToken.None);
            _listener.Dispose();
            await _host.DisposeAsync();
        }
        finally
        {
            DeleteStateDirectory();
        }
    }

    /// <summary>
    ///     This method removes the state directory with the certificate files the run wrote.
    /// </summary>
    private void DeleteStateDirectory()
    {
        if (_stateDirectory != null && Directory.Exists(_stateDirectory))
            Directory.Delete(_stateDirectory, recursive: true);
    }

    /// <summary>
    ///     This method sends 64 queries back to back on the open connection and reads the 64
    ///     replies.
    /// </summary>
    [Benchmark(OperationsPerInvoke = Pipelined)]
    public async Task<int> Pipelined64OnOneConnection() => (await RoundTripBatchAsync()).Count;

    /// <summary>
    ///     This method opens a connection, completes the TLS handshake, sends one query, reads
    ///     the reply and closes the connection.
    /// </summary>
    [Benchmark]
    public async Task<int> OneQueryPerNewConnection()
    {
        await using SslStream stream = await ConnectAsync();
        await stream.WriteAsync(_singleRequest);
        await stream.FlushAsync();
        await ReadFrameAsync(stream);
        return 1;
    }

    /// <summary>
    ///     This method connects with <c>NoDelay</c> set (the client side of the exchange must not
    ///     add a delay of its own) and accepts the listener's self-signed certificate.
    /// </summary>
    private async Task<SslStream> ConnectAsync()
    {
        Socket socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true,
        };
        await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, _port));
        SslStream stream = new(
            new NetworkStream(socket, ownsSocket: true),
            leaveInnerStreamOpen: false,
            (_, _, _, _) => true
        );
        await stream.AuthenticateAsClientAsync("localhost");
        return stream;
    }

    private async Task<HashSet<int>> RoundTripBatchAsync()
    {
        await _connection.WriteAsync(_pipelinedRequest);
        await _connection.FlushAsync();
        HashSet<int> ids = [];

        for (int reply = 0; reply < Pipelined; reply++)
            ids.Add(await ReadFrameAsync(_connection));

        return ids;
    }

    /// <summary>
    ///     This method reads one length-prefixed reply and returns its query ID; it throws when
    ///     the reply is not a NOERROR answer with one record.
    /// </summary>
    private async Task<int> ReadFrameAsync(SslStream stream)
    {
        await stream.ReadExactlyAsync(_buffer.AsMemory(0, 2));
        int length = BinaryPrimitives.ReadUInt16BigEndian(_buffer);
        byte[] frame = new byte[length];
        await stream.ReadExactlyAsync(frame);

        if ((frame[3] & 0x0F) != 0 || BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(6)) != 1)
            throw new InvalidOperationException("A TLS reply was not the expected answer.");

        return BinaryPrimitives.ReadUInt16BigEndian(frame);
    }

    private static byte[] Frame(byte[] message)
    {
        byte[] framed = new byte[message.Length + 2];
        BinaryPrimitives.WriteUInt16BigEndian(framed, (ushort)message.Length);
        message.CopyTo(framed, 2);
        return framed;
    }

    /// <summary>
    ///     This class is an <see cref="IOptionsMonitor{TOptions}" /> that always returns one
    ///     value; the listener reads its settings through it.
    /// </summary>
    private sealed class FixedOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        /// <inheritdoc />
        public T CurrentValue => value;

        /// <inheritdoc />
        public T Get(string name) => value;

        /// <inheritdoc />
        public IDisposable OnChange(Action<T, string> listener) => null;
    }
}
