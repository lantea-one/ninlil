using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Benchmarks.Pipeline;
using Ninlil.Benchmarks.Storage;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Protocol.DnsOverHttps;
using Ninlil.Dns.Server.Core.Certificates;

// Define our namespace.
namespace Ninlil.Benchmarks.Transport;

/// <summary>
///     This class measures a DNS query over DNS-over-HTTPS (RFC 8484, POST form) through a real
///     <see cref="DnsOverHttpsHostedService" /> (its own Kestrel host) on <c>127.0.0.1</c> and an
///     OS-assigned port, with the full pipeline of <see cref="PipelineBenchmarkHost" /> behind it
///     (every engine on, data set at the <c>--scale</c>). The certificate is self-signed and kept
///     in a temporary directory that the cleanup deletes. The client is an
///     <see cref="HttpClient" /> that accepts that certificate and speaks HTTP/2, as the
///     repository's <c>HttpsForwarderClient</c> does; that class cannot be used itself because it
///     validates against the operating system's trust store. Two scenarios: 64 POST requests in
///     flight at once on one HTTP/2 connection (the DoH counterpart of pipelining), and one POST on
///     a new client, so a new TCP and TLS connection. The query ID is 0 on every request (RFC 8484
///     §4.1), so the setup checks that all 64 replies are the expected one-record answer instead
///     of counting IDs. The mean is per query.
/// </summary>
[Config(typeof(StorageConfig))]
public class DohTransportBenchmarks
{
    private const int Concurrent = 64;
    private const string LogLevelVariable = "Logging__LogLevel__Default";
    private const string DnsMessageContentType = "application/dns-message";

    private PipelineBenchmarkHost _host;
    private DnsOverHttpsHostedService _service;
    private string _stateDirectory;
    private Uri _uri;
    private byte[] _request;
    private HttpClient _client;
    private string _previousLogLevel;

    /// <summary>
    ///     This method starts the host and the service, opens the long-lived HTTP/2 connection and
    ///     checks one batch of concurrent requests.
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

        int port;

        using (Socket probe = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
        {
            probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            port = ((IPEndPoint)probe.LocalEndPoint).Port;
        }

        _stateDirectory = Path.Combine(Path.GetTempPath(), $"ninlil-bench-doh-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_stateDirectory);

        DnsOverHttpsOptions options = new()
        {
            Enable = true,
            Port = port,
            Certificate = new TlsCertificateOptions
            {
                SelfSignedCertificateStatePath = Path.Combine(_stateDirectory, "doh.pfx"),
                SelfSignedSubjectAlternativeNames = ["localhost"],
            },
        };
        // The service builds its own web host, which would log every request at Information level
        // to the console and distort the timings. The host reads this variable as configuration.
        _previousLogLevel = Environment.GetEnvironmentVariable(LogLevelVariable);
        Environment.SetEnvironmentVariable(LogLevelVariable, "Warning");
        _service = new DnsOverHttpsHostedService(
            _host.Pipeline,
            Options.Create(options),
            _host.Services.GetRequiredService<ILogger<DnsOverHttpsHostedService>>()
        );
        await _service.StartAsync(CancellationToken.None);
        Environment.SetEnvironmentVariable(LogLevelVariable, _previousLogLevel);

        _uri = new Uri($"https://127.0.0.1:{port}{options.Path}");
        _request = PipelineBenchmarkHost.BuildQuery(
            _host.NameFor(PipelineBenchmarkHost.ScenarioEnum.AuthoritativeHit),
            id: 0
        );
        _client = NewClient();

        if (await RunConcurrentAsync(_client) != Concurrent)
            throw new InvalidOperationException(
                "Benchmark setup check failed: not every concurrent DoH request was answered."
            );
    }

    /// <summary>
    ///     This method closes the client, stops the service and the host and deletes the
    ///     certificate directory.
    /// </summary>
    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        try
        {
            _client.Dispose();
            await _service.StopAsync(CancellationToken.None);
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
    ///     This method sends 64 POST requests at once on the open HTTP/2 connection and awaits
    ///     the 64 replies.
    /// </summary>
    [Benchmark(OperationsPerInvoke = Concurrent)]
    public async Task<int> Concurrent64OnOneConnection() => await RunConcurrentAsync(_client);

    /// <summary>
    ///     This method creates a client, so a new connection and TLS handshake, sends one POST,
    ///     reads the reply and disposes the client.
    /// </summary>
    [Benchmark]
    public async Task<int> OneQueryPerNewConnection()
    {
        using HttpClient client = NewClient();
        return await PostAsync(client);
    }

    private HttpClient NewClient() =>
        new(
            new SocketsHttpHandler
            {
                SslOptions = { RemoteCertificateValidationCallback = (_, _, _, _) => true },
            },
            disposeHandler: true
        )
        {
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
        };

    private async Task<int> RunConcurrentAsync(HttpClient client)
    {
        Task<int>[] requests = new Task<int>[Concurrent];

        for (int request = 0; request < Concurrent; request++)
            requests[request] = PostAsync(client);

        return (await Task.WhenAll(requests)).Sum();
    }

    /// <summary>
    ///     This method posts the query and returns 1 when the reply is a NOERROR answer with one
    ///     record; it throws otherwise.
    /// </summary>
    private async Task<int> PostAsync(HttpClient client)
    {
        using ByteArrayContent content = new(_request);
        content.Headers.ContentType = new MediaTypeHeaderValue(DnsMessageContentType);
        using HttpResponseMessage response = await client.PostAsync(_uri, content);
        response.EnsureSuccessStatusCode();
        byte[] body = await response.Content.ReadAsByteArrayAsync();

        if ((body[3] & 0x0F) != 0 || (body[6] << 8 | body[7]) != 1)
            throw new InvalidOperationException("A DoH reply was not the expected answer.");

        return 1;
    }
}
