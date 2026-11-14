using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ninlil.Benchmarks.Pipeline;
using Ninlil.Benchmarks.Storage;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Protocol.DnsCrypt;
using Ninlil.Dns.Server.DnsCrypt;
using Ninlil.Dns.Server.Dnssec.Algorithms;

// Define our namespace.
namespace Ninlil.Benchmarks.Transport;

/// <summary>
///     This class measures an encrypted DNS query over DNSCrypt TCP through a real
///     <see cref="DnsCryptTcpListener" /> on <c>127.0.0.1</c>, with the full pipeline of
///     <see cref="PipelineBenchmarkHost" /> behind it (data set at the <c>--scale</c>). DNSCrypt
///     over TCP carries one transaction per connection (the resolver closes after its answer),
///     so a query cannot be pipelined and every operation opens a connection. Two benchmark
///     methods, each reported per operation and run by <see cref="Clients" /> concurrent callers
///     (1,280 operations per invocation):
///     <list type="bullet">
///         <item>
///             <see cref="Query_WithCertificateAlreadyKnown" />: one new connection that carries
///             one encrypted query and its reply. This is comparable to the plain TCP
///             benchmark's one query per new connection.
///         </item>
///         <item>
///             <see cref="Query_WithCertificateDiscovery" />: a new connection for the plaintext
///             certificate query (RFC 1035 two-octet framing, the signature checked against the
///             provider key), then one for the encrypted query. This is the full exchange that
///             <see cref="DnsCryptUdpTransportBenchmarks" /> measures per operation, over
///             TCP.
///         </item>
///     </list>
///     The client is written here, because the repository's forwarder client uses TCP only after
///     UDP has failed. It sets <c>NoDelay</c> and writes the length prefix and the packet in one
///     send, like the plain TCP benchmark, and closes with a zero linger time so the run does not
///     fill the local port range with connections in TIME_WAIT. The allocation column includes
///     the client's cryptography. The certificate comes from
///     <see cref="DnsCryptBenchmarkProvider" /> over a state file in a temporary directory that
///     the cleanup deletes. The setup checks that each caller's reply is the expected
///     single-answer A response with its query ID and that 100 further encrypted queries over the
///     transport run zero database commands and reach no upstream.
/// </summary>
[Config(typeof(StorageConfig))]
public class DnsCryptTcpTransportBenchmarks
{
    private const int Operations = 1280;

    // A listener port that another program takes between the reservation and the bind is retried
    // this many times; DnsCryptTcpListener does not expose the port it bound, so the port cannot
    // come from a bind to port 0.
    private const int BindAttempts = 5;

    private PipelineBenchmarkHost _host;
    private DnsCryptBenchmarkProvider _provider;
    private DnsCryptTcpListener _listener;
    private IDnssecSigningAlgorithm _algorithm;
    private IPEndPoint _endpoint;
    private byte[] _providerPublicKey;
    private DnsCryptCertificate _certificate;
    private DnsMessage[] _queries;
    private byte[] _discoveryRequest;

    /// <summary>
    ///     This property contains the number of callers that query at the same time;
    ///     BenchmarkDotNet sets it.
    /// </summary>
    [Params(1, 64)]
    public int Clients { get; set; }

    /// <summary>
    ///     This method starts the host, the certificate rotation and the listener, and checks
    ///     one query per caller plus 100 warm queries.
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
        await _host.ExpectNoDatabaseCommandsAsync();

        _provider = await DnsCryptBenchmarkProvider.StartAsync(_host, 0);
        int port = await StartListenerAsync();
        _endpoint = new IPEndPoint(IPAddress.Loopback, port);
        _providerPublicKey = _provider.Store.ProviderKeyPair.PublicKeyWireFormat;

        if (!DnssecAlgorithmRegistry.TryGetAlgorithm(DnssecAlgorithmEnum.Ed25519, out _algorithm))
            throw new InvalidOperationException("The Ed25519 algorithm is not registered.");

        // The plaintext certificate query (DNSCrypt v2 section 12): a TXT query for
        // 2.dnscrypt-cert.<provider name>, with no recursion.
        DnsMessage discovery = new()
        {
            Id = 0x4c4c,
            Opcode = DnsOpcodeEnum.Query,
            RecursionDesired = false,
            Questions =
            {
                new DnsQuestion(
                    DnsName.Parse($"2.dnscrypt-cert.{DnsCryptBenchmarkProvider.ProviderName}"),
                    DnsResourceRecordTypeEnum.Txt,
                    DnsClassEnum.Internet
                ),
            },
        };
        _discoveryRequest = DnsMessageWriter.Write(discovery);
        _certificate = await DiscoverAsync();

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

            DnsMessage reply = await QueryAsync(_certificate, _queries[client]);

            if (
                reply.Id != client + 1
                || reply.ResponseCode != DnsResponseCodeEnum.NoError
                || reply.Answers.Count != 1
            )
                throw new InvalidOperationException(
                    $"Benchmark setup check failed: caller {client} got a wrong reply over DNSCrypt TCP."
                );
        }

        // The pipeline's caches must answer a query that arrives over this transport too.
        int commandsBefore = _host.Commands.Count;
        long upstreamBefore = _host.Upstream.ReceivedCount;

        for (int index = 0; index < 100; index++)
            await QueryAsync(_certificate, _queries[0]);

        if (_host.Commands.Count != commandsBefore)
            throw new InvalidOperationException(
                $"Benchmark setup check failed: {_host.Commands.Count - commandsBefore} database command(s) ran for 100 warm DNSCrypt TCP queries."
            );

        if (_host.Upstream.ReceivedCount != upstreamBefore)
            throw new InvalidOperationException(
                "Benchmark setup check failed: a warm DNSCrypt TCP query reached the fake upstream."
            );
    }

    /// <summary>
    ///     This method reserves a loopback TCP port and starts the listener on it, trying again
    ///     with a new port when the listener could not bind. It returns the port in use.
    /// </summary>
    private async Task<int> StartListenerAsync()
    {
        for (int attempt = 1; ; attempt++)
        {
            int port = FreeTcpPort();
            _provider.Options.CurrentValue.Port = port;
            _listener = new DnsCryptTcpListener(
                _host.Pipeline,
                _provider.Store,
                _provider.Options,
                _provider.Services.GetRequiredService<ILogger<DnsCryptTcpListener>>()
            );
            await _listener.StartAsync(CancellationToken.None);

            // BackgroundService binds on a continuation after StartAsync returns; the listener
            // reports either success or the addresses it could not bind.
            for (
                int wait = 0;
                wait < 500 && !_listener.IsListening && _listener.FailedListenAddresses.Count == 0;
                wait++
            )
                await Task.Delay(10);

            if (_listener.IsListening)
                return port;

            await _listener.StopAsync(CancellationToken.None);
            _listener.Dispose();

            if (attempt == BindAttempts)
                throw new InvalidOperationException("The DNSCrypt TCP listener did not start.");
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
    ///     This method runs 1,280 encrypted queries, each on its own connection with the
    ///     certificate already known, split over the callers that run at the same time.
    /// </summary>
    [Benchmark(OperationsPerInvoke = Operations)]
    public Task<int> Query_WithCertificateAlreadyKnown() => RunAsync(discover: false);

    /// <summary>
    ///     This method runs 1,280 full exchanges (a certificate query on one connection, the
    ///     encrypted query on another), split over the callers that run at the same time.
    /// </summary>
    [Benchmark(OperationsPerInvoke = Operations)]
    public Task<int> Query_WithCertificateDiscovery() => RunAsync(discover: true);

    private async Task<int> RunAsync(bool discover)
    {
        int each = Operations / Clients;
        Task<int>[] callers = new Task<int>[Clients];

        for (int client = 0; client < Clients; client++)
            callers[client] = RunClientAsync(client, each, discover);

        return (await Task.WhenAll(callers)).Sum();
    }

    private async Task<int> RunClientAsync(int client, int rounds, bool discover)
    {
        await Task.Yield();
        int answers = 0;

        for (int round = 0; round < rounds; round++)
        {
            DnsCryptCertificate certificate = discover ? await DiscoverAsync() : _certificate;
            answers += (await QueryAsync(certificate, _queries[client])).Answers.Count;
        }

        return answers;
    }

    /// <summary>
    ///     This method asks for the certificate over a new connection and returns the valid one
    ///     with the highest serial whose Ed25519 signature the provider key verifies.
    /// </summary>
    private async Task<DnsCryptCertificate> DiscoverAsync()
    {
        DnsMessage reply = DnsMessageReader.Read(await ExchangeAsync(_discoveryRequest));
        DnsCryptCertificate best = null;
        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (DnsResourceRecord answer in reply.Answers)
        {
            // One RFC 1035 §3.3 character-string: a length byte, then the certificate bytes.
            // The RDATA is read raw because the text codec goes through UTF-8 and would corrupt
            // binary data.
            if (answer.Type != DnsResourceRecordTypeEnum.Txt || answer.Data.Length < 1)
                continue;

            int length = answer.Data[0];

            if (answer.Data.Length < 1 + length)
                continue;

            DnsCryptCertificate candidate = DnsCryptCertificateCodec.Decode(
                answer.Data.AsSpan(1, length)
            );

            if (
                candidate.IsValidAt(now)
                && _algorithm.Verify(
                    DnsCryptCertificateCodec.BuildSignedData(candidate),
                    candidate.Signature,
                    _providerPublicKey
                )
                && (best is null || candidate.Serial > best.Serial)
            )
                best = candidate;
        }

        return best
            ?? throw new InvalidOperationException("No valid DNSCrypt certificate was returned.");
    }

    /// <summary>
    ///     This method encrypts the query with a fresh client key pair and nonce half, sends it
    ///     over a new connection and returns the decrypted reply. It throws when the reply does
    ///     not answer the query.
    /// </summary>
    private async Task<DnsMessage> QueryAsync(DnsCryptCertificate certificate, DnsMessage query)
    {
        (byte[] clientPublicKey, byte[] clientSecretKey) = DnsCryptCryptoBox.GenerateKeyPair(
            certificate.EsVersion
        );
        byte[] clientNonceHalf = RandomNumberGenerator.GetBytes(
            DnsCryptEnvelopeCodec.HalfNonceLength
        );
        byte[] packet = DnsCryptEnvelopeCodec.EncryptQuery(
            certificate.EsVersion,
            certificate.ClientMagic,
            clientPublicKey,
            clientSecretKey,
            certificate.ResolverPublicKey,
            clientNonceHalf,
            DnsMessageWriter.Write(query),
            overTcp: true
        );

        byte[] response = await ExchangeAsync(packet);
        DnsMessage reply = DnsMessageReader.Read(
            DnsCryptEnvelopeCodec.DecryptResponse(
                certificate.EsVersion,
                response,
                clientNonceHalf,
                clientSecretKey,
                certificate.ResolverPublicKey
            )
        );

        return reply.Id == query.Id
            ? reply
            : throw new InvalidOperationException("The DNSCrypt reply answers another query.");
    }

    /// <summary>
    ///     This method opens a connection, sends one packet with its two-octet length prefix (the
    ///     same framing as RFC 1035 §4.2.2) and returns the one length-prefixed packet that
    ///     comes back.
    /// </summary>
    private async Task<byte[]> ExchangeAsync(byte[] packet)
    {
        // NoDelay: a small request and a small reply on a fresh connection must not wait for an
        // acknowledgement. A zero linger time resets the connection on close, which keeps the
        // run out of TIME_WAIT.
        using Socket socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true,
            LingerState = new LingerOption(true, 0),
        };
        await socket.ConnectAsync(_endpoint);

        byte[] framed = new byte[packet.Length + 2];
        BinaryPrimitives.WriteUInt16BigEndian(framed, (ushort)packet.Length);
        packet.CopyTo(framed, 2);
        await socket.SendAsync(framed);

        byte[] prefix = new byte[2];
        await ReadExactlyAsync(socket, prefix);
        byte[] response = new byte[BinaryPrimitives.ReadUInt16BigEndian(prefix)];
        await ReadExactlyAsync(socket, response);
        return response;
    }

    private static async Task ReadExactlyAsync(Socket socket, byte[] buffer)
    {
        int read = 0;

        while (read < buffer.Length)
        {
            int received = await socket.ReceiveAsync(buffer.AsMemory(read));

            if (received == 0)
                throw new InvalidOperationException("The server closed the connection.");

            read += received;
        }
    }

    private static int FreeTcpPort()
    {
        using Socket probe = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.LocalEndPoint).Port;
    }
}
