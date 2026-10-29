using System.Buffers;
using System.Buffers.Text;
using System.Net;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Benchmarks.Pipeline;
using Ninlil.Benchmarks.Storage;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Protocol.DnsCrypt;
using Ninlil.Dns.Server.Core.Forwarding;
using Ninlil.Dns.Server.DnsCrypt;
using Ninlil.Dns.Server.Forwarding.Clients;
using Ninlil.Dns.Server.Forwarding.Definition;

// Define our namespace.
namespace Ninlil.Benchmarks.Transport;

/// <summary>
///     This class measures an encrypted DNS query over DNSCrypt UDP through a real
///     <see cref="DnsCryptUdpListener" /> on <c>127.0.0.1</c> and an OS-assigned port, with the
///     full pipeline of <see cref="PipelineBenchmarkHost" /> behind it (data set at the
///     <c>--scale</c>). The client is the repository's own <see cref="DnsCryptForwarderClient" />,
///     which discovers the certificate with a plaintext TXT query and then sends the encrypted
///     query, so one operation is two datagram round trips plus the client's cryptography; the
///     allocation column therefore includes the client. The signed certificate comes from
///     <see cref="DnsCryptCertificateRotationService" /> over a state file in a temporary
///     directory that the cleanup deletes. 1,280 operations run per invocation, split over
///     <see cref="Clients" /> concurrent callers. The setup checks that the reply is the
///     expected single-answer A response with the caller's query ID.
/// </summary>
[Config(typeof(StorageConfig))]
public class DnsCryptUdpTransportBenchmarks
{
    private const int Operations = 1280;
    private const string ProviderName = "bench.example";

    private PipelineBenchmarkHost _host;
    private string _stateDirectory;
    private DnsCryptCertificateRotationService _rotation;
    private DnsCryptUdpListener _listener;
    private DnsCryptForwarderClient _client;
    private UpstreamForwarderDefinition _definition;
    private DnsMessage[] _queries;

    /// <summary>
    ///     This property contains the number of callers that query at the same time;
    ///     BenchmarkDotNet sets it.
    /// </summary>
    [Params(1, 64)]
    public int Clients { get; set; }

    /// <summary>
    ///     This method starts the host, the certificate rotation and the listener, and checks
    ///     one query per caller.
    /// </summary>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        _host = await PipelineBenchmarkHost.StartAsync(
            PipelineBenchmarkHost.FeatureSetEnum.Full,
            BenchmarkEnvironment.Scale
        );
        await _host.WarmUpAndCheckAsync();

        _stateDirectory = Path.Combine(
            Path.GetTempPath(),
            $"ninlil-bench-dnscrypt-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(_stateDirectory);

        int port = FreeUdpPort();
        DnsCryptOptions dnsCrypt = new()
        {
            Enable = true,
            Port = port,
            ListenAddresses = "127.0.0.1",
            ProviderName = ProviderName,
            CertificateStateFilePath = Path.Combine(_stateDirectory, "state.json"),
            EnabledEsVersions = [DnsCryptEsVersion.XChaCha20Poly1305],
            CertificateRotationInterval = TimeSpan.FromMinutes(10),
            CertificateValidityDuration = TimeSpan.FromMinutes(20),
            CertificateClockSkewTolerance = TimeSpan.Zero,
        };
        ServiceCollection services = new();
        services.AddSingleton(Options.Create(dnsCrypt));
        services.AddSingleton<IOptionsMonitor<DnsCryptOptions>>(new FixedOptionsMonitor(dnsCrypt));
        services.AddSingleton<ILoggerFactory>(_host.Services.GetRequiredService<ILoggerFactory>());
        services.AddLogging();
        services.AddSingleton<FileDnsCryptCertificateStore>();
        ServiceProvider provider = services.BuildServiceProvider();
        FileDnsCryptCertificateStore store =
            provider.GetRequiredService<FileDnsCryptCertificateStore>();

        _rotation = new DnsCryptCertificateRotationService(
            store,
            provider.GetRequiredService<IOptionsMonitor<DnsCryptOptions>>(),
            provider.GetRequiredService<ILogger<DnsCryptCertificateRotationService>>()
        );
        await _rotation.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => store.Entries.Count > 0);

        _listener = new DnsCryptUdpListener(
            _host.Pipeline,
            store,
            provider.GetRequiredService<IOptionsMonitor<DnsCryptOptions>>(),
            provider.GetRequiredService<ILogger<DnsCryptUdpListener>>()
        );
        await _listener.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => _listener.IsListening);

        _definition = DnsCryptStampParser.Parse(
            BuildStamp($"127.0.0.1:{port}", store.ProviderKeyPair.PublicKeyWireFormat)
        );
        _client = new DnsCryptForwarderClient();

        DnsName name = _host.NameFor(PipelineBenchmarkHost.ScenarioEnum.AuthoritativeHit);
        _queries = new DnsMessage[Clients];

        for (int client = 0; client < Clients; client++)
        {
            _queries[client] = new DnsMessage
            {
                Id = (ushort)(client + 1),
                RecursionDesired = true,
                Questions =
                {
                    new DnsQuestion(name, DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet),
                },
            };

            DnsMessage reply = await _client.ForwardAsync(_definition, _queries[client]);

            if (
                reply.Id != client + 1
                || reply.ResponseCode != DnsResponseCodeEnum.NoError
                || reply.Answers.Count != 1
            )
                throw new InvalidOperationException(
                    $"Benchmark setup check failed: caller {client} got a wrong reply over DNSCrypt UDP."
                );
        }
    }

    /// <summary>
    ///     This method stops the listener, the rotation and the host and deletes the state
    ///     directory.
    /// </summary>
    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        await _listener.StopAsync(CancellationToken.None);
        _listener.Dispose();
        await _rotation.StopAsync(CancellationToken.None);
        await _host.DisposeAsync();

        if (Directory.Exists(_stateDirectory))
            Directory.Delete(_stateDirectory, recursive: true);
    }

    /// <summary>
    ///     This method runs 1,280 encrypted queries, split over the callers that run at the same
    ///     time.
    /// </summary>
    [Benchmark(OperationsPerInvoke = Operations)]
    public async Task<int> Queries_AllClientsAtOnce()
    {
        int each = Operations / Clients;
        Task<int>[] callers = new Task<int>[Clients];

        for (int client = 0; client < Clients; client++)
            callers[client] = RunClientAsync(client, each);

        return (await Task.WhenAll(callers)).Sum();
    }

    private async Task<int> RunClientAsync(int client, int rounds)
    {
        await Task.Yield();
        int answers = 0;

        for (int round = 0; round < rounds; round++)
            answers += (await _client.ForwardAsync(_definition, _queries[client])).Answers.Count;

        return answers;
    }

    /// <summary>
    ///     This method builds the <c>sdns://</c> stamp of a DNSCrypt resolver at
    ///     <paramref name="address" /> (a host:port) with the given provider public key.
    /// </summary>
    private static string BuildStamp(string address, byte[] providerPublicKey)
    {
        ArrayBufferWriter<byte> buffer = new();
        buffer.GetSpan(1)[0] = 0x01;
        buffer.Advance(1);
        buffer.GetSpan(8).Clear();
        buffer.Advance(8);

        WriteLengthPrefixed(buffer, System.Text.Encoding.ASCII.GetBytes(address));
        WriteLengthPrefixed(buffer, providerPublicKey);
        WriteLengthPrefixed(
            buffer,
            System.Text.Encoding.UTF8.GetBytes($"2.dnscrypt-cert.{ProviderName}")
        );

        return "sdns://" + Base64Url.EncodeToString(buffer.WrittenSpan);
    }

    private static void WriteLengthPrefixed(IBufferWriter<byte> buffer, byte[] value)
    {
        buffer.GetSpan(1)[0] = (byte)value.Length;
        buffer.Advance(1);
        value.CopyTo(buffer.GetSpan(value.Length));
        buffer.Advance(value.Length);
    }

    private static int FreeUdpPort()
    {
        using System.Net.Sockets.Socket probe = new(
            System.Net.Sockets.AddressFamily.InterNetwork,
            System.Net.Sockets.SocketType.Dgram,
            System.Net.Sockets.ProtocolType.Udp
        );
        probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.LocalEndPoint).Port;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        // BackgroundService binds and generates on a continuation after StartAsync returns.
        for (int attempt = 0; attempt < 500 && !condition(); attempt++)
            await Task.Delay(10);

        if (!condition())
            throw new InvalidOperationException("The DNSCrypt listener did not start.");
    }

    /// <summary>
    ///     This class is an <see cref="IOptionsMonitor{TOptions}" /> that always returns one
    ///     value; the listener and the rotation service read their settings through it.
    /// </summary>
    private sealed class FixedOptionsMonitor(DnsCryptOptions value)
        : IOptionsMonitor<DnsCryptOptions>
    {
        /// <inheritdoc />
        public DnsCryptOptions CurrentValue => value;

        /// <inheritdoc />
        public DnsCryptOptions Get(string name) => value;

        /// <inheritdoc />
        public IDisposable OnChange(Action<DnsCryptOptions, string> listener) => null;
    }
}
