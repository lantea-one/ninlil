using System.Buffers.Binary;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.Versioning;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Benchmarks.Pipeline;
using Ninlil.Benchmarks.Storage;
using Ninlil.Dns.Protocol.DnsOverQuic;
using Ninlil.Dns.Server.Core.Certificates;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.RateLimiting;

// Define our namespace.
namespace Ninlil.Benchmarks.Transport;

/// <summary>
///     This class measures a DNS query over DNS-over-QUIC (RFC 9250) through a real
///     <see cref="DnsOverQuicListener" /> on <c>127.0.0.1</c> and an OS-assigned UDP port, with
///     the full pipeline of <see cref="PipelineBenchmarkHost" /> behind it (every engine on, data
///     set at the <c>--scale</c>). The certificate is self-signed and kept in a temporary
///     directory that the cleanup deletes. The client is a plain <see cref="QuicConnection" />
///     that accepts that certificate: the repository's <c>QuicForwarderClient</c> validates
///     against the operating system's trust store and opens a connection per query. Two
///     scenarios: 64 queries on 64 streams at once on one connection (one stream per query, as
///     RFC 9250 §4.2 has it), and one query on a new connection. The query ID is 0 on every
///     message (RFC 9250 §4.2.1), so the setup checks that every reply is the expected one-record
///     answer. The mean is per query. When QUIC is not supported on the host (no
///     <c>libmsquic</c>), the setup prints a message and every benchmark returns at once.
/// </summary>
[Config(typeof(StorageConfig))]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
[SupportedOSPlatform("windows")]
public class DoqTransportBenchmarks
{
    private const int Concurrent = 64;
    private static readonly SslApplicationProtocol DoqProtocol = new("doq");

    private bool _supported;
    private PipelineBenchmarkHost _host;
    private DnsOverQuicListener _listener;
    private string _stateDirectory;
    private int _port;
    private byte[] _framedRequest;
    private QuicConnection _connection;

    /// <summary>
    ///     This method starts the host and the listener, opens the long-lived connection and
    ///     checks one batch of concurrent streams. It does nothing but print a message when QUIC
    ///     is not supported.
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
        _supported = QuicListener.IsSupported && QuicConnection.IsSupported;

        if (!_supported)
        {
            Console.WriteLine(
                "DoqTransportBenchmarks skipped: QUIC is not supported on this host (libmsquic is missing)."
            );
            return;
        }

        _host = await PipelineBenchmarkHost.StartAsync(
            PipelineBenchmarkHost.FeatureSetEnum.Full,
            BenchmarkEnvironment.Scale
        );
        await _host.WarmUpAndCheckAsync();

        using (Socket probe = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
        {
            probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            _port = ((IPEndPoint)probe.LocalEndPoint).Port;
        }

        _stateDirectory = Path.Combine(Path.GetTempPath(), $"ninlil-bench-doq-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_stateDirectory);

        DnsOverQuicOptions options = new()
        {
            Enable = true,
            Port = _port,
            ListenAddresses = "127.0.0.1",
            Certificate = new TlsCertificateOptions
            {
                SelfSignedCertificateStatePath = Path.Combine(_stateDirectory, "doq.pfx"),
                SelfSignedSubjectAlternativeNames = ["localhost"],
            },
        };
        _listener = new DnsOverQuicListener(
            _host.Pipeline,
            _host.Services.GetRequiredService<IServiceScopeFactory>(),
            new FixedOptionsMonitor<DnsOverQuicOptions>(options),
            _host.Services.GetRequiredService<ILogger<DnsOverQuicListener>>(),
            _host.Services.GetService<IDnsRateLimiter>(),
            _host.Services.GetService<IDnsServerEventPublisher>()
        );
        await _listener.StartAsync(CancellationToken.None);

        // BackgroundService starts the QUIC listener on a continuation after StartAsync returns.
        for (int attempt = 0; attempt < 500 && !_listener.IsListening; attempt++)
            await Task.Delay(10);

        if (!_listener.IsListening)
            throw new InvalidOperationException("The listener did not start.");

        byte[] query = PipelineBenchmarkHost.BuildQuery(
            _host.NameFor(PipelineBenchmarkHost.ScenarioEnum.AuthoritativeHit),
            id: 0
        );
        _framedRequest = new byte[2 + query.Length];
        BinaryPrimitives.WriteUInt16BigEndian(_framedRequest, (ushort)query.Length);
        query.CopyTo(_framedRequest, 2);

        _connection = await ConnectAsync();

        if (await RunConcurrentAsync() != Concurrent)
            throw new InvalidOperationException(
                "Benchmark setup check failed: not every concurrent DoQ stream was answered."
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
            if (!_supported)
                return;

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
    ///     This method opens 64 streams at once on the open connection, one query each, and awaits
    ///     the 64 replies.
    /// </summary>
    [Benchmark(OperationsPerInvoke = Concurrent)]
    public async Task<int> Concurrent64StreamsOnOneConnection() =>
        _supported ? await RunConcurrentAsync() : 0;

    /// <summary>
    ///     This method opens a connection, sends one query on a new stream, reads the reply and
    ///     closes the connection.
    /// </summary>
    [Benchmark]
    public async Task<int> OneQueryPerNewConnection()
    {
        if (!_supported)
            return 0;

        await using QuicConnection connection = await ConnectAsync();
        return await QueryAsync(connection);
    }

    private async Task<QuicConnection> ConnectAsync() =>
        await QuicConnection.ConnectAsync(
            new QuicClientConnectionOptions
            {
                KeepAliveInterval = Timeout.InfiniteTimeSpan,
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, _port),
                ClientAuthenticationOptions = new SslClientAuthenticationOptions
                {
                    ApplicationProtocols = [DoqProtocol],
                    TargetHost = "localhost",
                    RemoteCertificateValidationCallback = (_, _, _, _) => true,
                },
                DefaultCloseErrorCode = 0,
                DefaultStreamErrorCode = 0,
            }
        );

    private async Task<int> RunConcurrentAsync()
    {
        Task<int>[] queries = new Task<int>[Concurrent];

        for (int query = 0; query < Concurrent; query++)
            queries[query] = QueryAsync(_connection);

        return (await Task.WhenAll(queries)).Sum();
    }

    /// <summary>
    ///     This method sends the query on a new stream and returns 1 when the reply is a NOERROR
    ///     answer with one record; it throws otherwise.
    /// </summary>
    private async Task<int> QueryAsync(QuicConnection connection)
    {
        await using QuicStream stream = await connection.OpenOutboundStreamAsync(
            QuicStreamType.Bidirectional
        );
        await stream.WriteAsync(_framedRequest, completeWrites: true);

        byte[] lengthPrefix = new byte[2];
        await stream.ReadExactlyAsync(lengthPrefix);
        byte[] reply = new byte[BinaryPrimitives.ReadUInt16BigEndian(lengthPrefix)];
        await stream.ReadExactlyAsync(reply);

        if ((reply[3] & 0x0F) != 0 || BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(6)) != 1)
            throw new InvalidOperationException("A DoQ reply was not the expected answer.");

        return 1;
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
