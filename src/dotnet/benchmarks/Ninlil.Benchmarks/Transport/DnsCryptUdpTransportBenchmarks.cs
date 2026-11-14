using System.Net;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    private PipelineBenchmarkHost _host;
    private DnsCryptBenchmarkProvider _provider;
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
        try
        {
            await StartAsync();
        }
        catch
        {
            // A failed setup never reaches the cleanup method, so the state directory goes here.
            _provider?.DeleteStateDirectory();
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

        int port = FreeUdpPort();
        _provider = await DnsCryptBenchmarkProvider.StartAsync(_host, port);

        _listener = new DnsCryptUdpListener(
            _host.Pipeline,
            _provider.Store,
            _provider.Options,
            _provider.Services.GetRequiredService<ILogger<DnsCryptUdpListener>>()
        );
        await _listener.StartAsync(CancellationToken.None);
        await DnsCryptBenchmarkProvider.WaitUntilAsync(() => _listener.IsListening);

        _definition = DnsCryptStampParser.Parse(_provider.BuildStamp($"127.0.0.1:{port}"));
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
        try
        {
            await _listener.StopAsync(CancellationToken.None);
            _listener.Dispose();
            await _provider.StopRotationAsync();
            await _host.DisposeAsync();
        }
        finally
        {
            _provider.DeleteStateDirectory();
        }
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
}
