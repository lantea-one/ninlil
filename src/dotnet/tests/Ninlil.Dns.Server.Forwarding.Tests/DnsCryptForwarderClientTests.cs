using System.Buffers;
using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Protocol.DnsCrypt;
using Ninlil.Dns.Server.Core.Forwarding;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.DnsCrypt;
using Ninlil.Dns.Server.Forwarding.Clients;
using Ninlil.Dns.Server.Forwarding.Definition;
using Ninlil.Dns.Server.Forwarding.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Tests;

/// <summary>
///     This class tests <see cref="DnsCryptForwarderClient" /> and <see cref="DnsCryptStampParser" />
///     end to end against Ninlil's <see cref="DnsCryptUdpListener" /> on a loopback port, standing
///     in for a third-party DNSCrypt resolver. The round trip uses sockets, X25519 and Ed25519
///     crypto and certificate discovery, with no mocks; a fixed-answer dispatcher sits behind the
///     listener. The truncation and receive-loop tests also run a <see cref="DnsCryptTcpListener" />
///     on the same port, reached through a <see cref="LoopbackRelay" /> that counts accepted TCP
///     connections and can inject garbage datagrams before the real UDP reply.
/// </summary>
public class DnsCryptForwarderClientTests
{
    private sealed class FixedAnswerDispatcher(DnsMessage response) : IDnsQueryDispatcher
    {
        public ValueTask<DnsMessage> DispatchAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        )
        {
            DnsMessage answered = new()
            {
                Id = context.Request.Id,
                IsResponse = response.IsResponse,
                Opcode = response.Opcode,
                ResponseCode = response.ResponseCode,
            };
            // A real resolver repeats the question; the client rejects a reply without it.
            answered.Questions.AddRange(context.Request.Questions);
            answered.Answers.AddRange(response.Answers);
            return ValueTask.FromResult(answered);
        }
    }

    private static int GetEphemeralUdpPort()
    {
        using System.Net.Sockets.Socket socket = new(
            System.Net.Sockets.AddressFamily.InterNetwork,
            System.Net.Sockets.SocketType.Dgram,
            System.Net.Sockets.ProtocolType.Udp
        );
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint).Port;
    }

    private static string BuildStamp(string address, byte[] providerPublicKey, string providerName)
    {
        ArrayBufferWriter<byte> buffer = new();
        buffer.GetSpan(1)[0] = 0x01;
        buffer.Advance(1);

        Span<byte> props = buffer.GetSpan(8);
        props.Clear();
        buffer.Advance(8);

        WriteLengthPrefixed(buffer, System.Text.Encoding.ASCII.GetBytes(address));
        WriteLengthPrefixed(buffer, providerPublicKey);
        WriteLengthPrefixed(buffer, System.Text.Encoding.UTF8.GetBytes(providerName));

        return "sdns://" + Base64Url.EncodeToString(buffer.WrittenSpan);
    }

    private static void WriteLengthPrefixed(IBufferWriter<byte> buffer, byte[] value)
    {
        buffer.GetSpan(1)[0] = (byte)value.Length;
        buffer.Advance(1);
        value.CopyTo(buffer.GetSpan(value.Length));
        buffer.Advance(value.Length);
    }

    [Fact]
    public async Task ForwardAsync_CompletesARealEncryptedRoundTripAgainstARealDnsCryptListener()
    {
        int port = GetEphemeralUdpPort();
        string statePath = Path.Combine(
            Path.GetTempPath(),
            $"forwarding-dnscrypt-test-{Guid.NewGuid():N}.json"
        );

        try
        {
            DnsCryptOptions options = new()
            {
                Enable = true,
                Port = port,
                ProviderName = "test.example",
                CertificateStateFilePath = statePath,
                EnabledEsVersions = [DnsCryptEsVersion.XChaCha20Poly1305],
                CertificateRotationInterval = TimeSpan.FromMinutes(10),
                CertificateValidityDuration = TimeSpan.FromMinutes(20),
                CertificateClockSkewTolerance = TimeSpan.Zero,
            };
            IOptions<DnsCryptOptions> optionsWrapped = Options.Create(options);
            IOptionsMonitor<DnsCryptOptions> optionsMonitor =
                new StaticOptionsMonitor<DnsCryptOptions>(options);

            FileDnsCryptCertificateStore store = new(
                optionsWrapped,
                NullLogger<FileDnsCryptCertificateStore>.Instance
            );
            DnsCryptCertificateRotationService rotationService = new(
                store,
                optionsMonitor,
                NullLogger<DnsCryptCertificateRotationService>.Instance
            );
            await rotationService.StartAsync(CancellationToken.None);

            for (int attempt = 0; attempt < 40 && store.Entries.Count < 1; attempt++)
                await Task.Delay(50);

            Assert.NotEmpty(store.Entries);

            DnsMessage cannedResponse = new()
            {
                IsResponse = true,
                Opcode = DnsOpcodeEnum.Query,
                ResponseCode = DnsResponseCodeEnum.NoError,
            };
            cannedResponse.Answers.Add(
                new DnsResourceRecord(
                    DnsName.Parse("forwarded.example."),
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet,
                    300,
                    [10, 20, 30, 40]
                )
            );

            DnsQueryProcessingPipeline pipeline = new(
                new FixedAnswerDispatcher(cannedResponse),
                new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                    new DnsQueryProcessingPipelineOptions()
                ),
                NullLogger<DnsQueryProcessingPipeline>.Instance
            );
            await pipeline.StartAsync(CancellationToken.None);

            DnsCryptUdpListener listener = new(
                pipeline,
                store,
                optionsMonitor,
                NullLogger<DnsCryptUdpListener>.Instance
            );
            await listener.StartAsync(CancellationToken.None);
            await Task.Delay(200);

            string stamp = BuildStamp(
                $"127.0.0.1:{port}",
                store.ProviderKeyPair.PublicKeyWireFormat,
                "2.dnscrypt-cert.test.example"
            );

            UpstreamForwarderDefinition definition = DnsCryptStampParser.Parse(stamp);

            Assert.Equal(UpstreamForwarderProtocolEnum.DnsCrypt, definition.Protocol);
            Assert.Equal(IPAddress.Loopback, definition.Address);
            Assert.Equal(port, definition.Port);
            Assert.Equal("2.dnscrypt-cert.test.example", definition.DnsCryptProviderName);

            DnsMessage query = new()
            {
                Id = 4242,
                Opcode = DnsOpcodeEnum.Query,
                RecursionDesired = true,
            };
            query.Questions.Add(
                new DnsQuestion(
                    DnsName.Parse("forwarded.example."),
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet
                )
            );

            DnsCryptForwarderClient client = new();
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));

            DnsMessage response = await client.ForwardAsync(definition, query, timeout.Token);

            Assert.Equal(4242, response.Id);
            Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
            Assert.Single(response.Answers);
            Assert.Equal(new IPAddress([10, 20, 30, 40]), new IPAddress(response.Answers[0].Data));

            await listener.StopAsync(CancellationToken.None);
            await pipeline.StopAsync(CancellationToken.None);
            await rotationService.StopAsync(CancellationToken.None);
        }
        finally
        {
            if (File.Exists(statePath))
                File.Delete(statePath);
        }
    }

    /// <summary>
    ///     This dispatcher answers every query with a fixed number of A records for the queried name
    ///     and records the ID of each query it sees, so a test can tell a UDP query from its TCP
    ///     retry.
    /// </summary>
    private sealed class RecordingDispatcher(int answerCount) : IDnsQueryDispatcher
    {
        /// <summary>
        ///     This property lists the message ID of every query that reached the dispatcher, in
        ///     arrival order.
        /// </summary>
        public ConcurrentQueue<ushort> QueryIds { get; } = new();

        /// <inheritdoc />
        public ValueTask<DnsMessage> DispatchAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        )
        {
            QueryIds.Enqueue(context.Request.Id);

            DnsMessage answered = new()
            {
                Id = context.Request.Id,
                IsResponse = true,
                Opcode = DnsOpcodeEnum.Query,
                ResponseCode = DnsResponseCodeEnum.NoError,
            };
            answered.Questions.AddRange(context.Request.Questions);

            for (int index = 0; index < answerCount; index++)
                answered.Answers.Add(
                    new DnsResourceRecord(
                        context.Request.Questions[0].Name,
                        DnsResourceRecordTypeEnum.A,
                        DnsClassEnum.Internet,
                        300,
                        [10, 0, 0, (byte)(index + 1)]
                    )
                );

            return ValueTask.FromResult(answered);
        }
    }

    /// <summary>
    ///     This class sits between the client and the real DNSCrypt listeners on one loopback port,
    ///     for UDP and TCP. It counts accepted TCP connections (the production TCP listener has no
    ///     counter) and, when asked, sends 50 random bytes to the client before each real UDP reply
    ///     of every client socket after the first (the first is certificate discovery, which takes
    ///     the first datagram by design). It is a relay, so garbage arrives from the same source
    ///     address the client expects.
    /// </summary>
    private sealed class LoopbackRelay : IAsyncDisposable
    {
        private readonly Socket udp;
        private readonly TcpListener tcp;
        private readonly IPEndPoint upstream;
        private readonly bool garbageBeforeUdpReplies;
        private readonly CancellationTokenSource stop = new();
        private readonly ConcurrentDictionary<EndPoint, Socket> sessions = new();
        private readonly List<Task> tasks = [];
        private int acceptedTcpConnections;
        private int sessionCount;

        /// <summary>
        ///     This constructor binds the relay's UDP and TCP sockets to one free loopback port and
        ///     starts forwarding to the listeners at <paramref name="upstream" />.
        /// </summary>
        /// <param name="upstream">The real listeners' loopback endpoint.</param>
        /// <param name="garbageBeforeUdpReplies">
        ///     Whether to send garbage ahead of each real UDP reply to the forwarded query.
        /// </param>
        public LoopbackRelay(IPEndPoint upstream, bool garbageBeforeUdpReplies)
        {
            this.upstream = upstream;
            this.garbageBeforeUdpReplies = garbageBeforeUdpReplies;

            // UDP and TCP share the stamp's one port: bind UDP to 0, then TCP to that port, and try
            // again when another process took the TCP side in between.
            for (int attempt = 0; ; attempt++)
            {
                Socket candidate = new(
                    AddressFamily.InterNetwork,
                    SocketType.Dgram,
                    ProtocolType.Udp
                );
                candidate.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                int port = ((IPEndPoint)candidate.LocalEndPoint).Port;
                TcpListener listener = new(IPAddress.Loopback, port);

                try
                {
                    listener.Start();
                }
                catch (SocketException) when (attempt < 10)
                {
                    candidate.Dispose();
                    continue;
                }

                udp = candidate;
                tcp = listener;
                break;
            }

            tasks.Add(Task.Run(() => UdpLoopAsync(stop.Token)));
            tasks.Add(Task.Run(() => TcpLoopAsync(stop.Token)));
        }

        /// <summary>
        ///     This property contains the port the client talks to.
        /// </summary>
        public int Port => ((IPEndPoint)udp.LocalEndPoint).Port;

        /// <summary>
        ///     This property contains how many TCP connections the relay has accepted.
        /// </summary>
        public int AcceptedTcpConnections => Volatile.Read(ref acceptedTcpConnections);

        /// <summary>
        ///     This method forwards each client datagram to the real listener, one upstream socket per
        ///     client socket.
        /// </summary>
        private async Task UdpLoopAsync(CancellationToken stoppingToken)
        {
            byte[] buffer = new byte[65527];

            try
            {
                while (true)
                {
                    SocketReceiveFromResult received = await udp.ReceiveFromAsync(
                        buffer,
                        new IPEndPoint(IPAddress.Any, 0),
                        stoppingToken
                    );
                    EndPoint client = received.RemoteEndPoint;

                    if (!sessions.TryGetValue(client, out Socket toUpstream))
                    {
                        // One upstream socket per client socket, so the listener sees one source and
                        // the replies find their way back.
                        bool injectGarbage =
                            garbageBeforeUdpReplies && Interlocked.Increment(ref sessionCount) > 1;
                        toUpstream = new Socket(
                            AddressFamily.InterNetwork,
                            SocketType.Dgram,
                            ProtocolType.Udp
                        );
                        sessions[client] = toUpstream;
                        Socket session = toUpstream;
                        tasks.Add(
                            Task.Run(() =>
                                UdpRepliesAsync(session, client, injectGarbage, stoppingToken)
                            )
                        );
                    }

                    await toUpstream.SendToAsync(
                        buffer.AsMemory(0, received.ReceivedBytes),
                        upstream,
                        stoppingToken
                    );
                }
            }
            catch (Exception exception)
                when (exception is OperationCanceledException or ObjectDisposedException) { }
        }

        /// <summary>
        ///     This method returns the listener's replies to one client socket, behind garbage when asked.
        /// </summary>
        private async Task UdpRepliesAsync(
            Socket fromUpstream,
            EndPoint client,
            bool injectGarbage,
            CancellationToken stoppingToken
        )
        {
            byte[] buffer = new byte[65527];

            try
            {
                while (true)
                {
                    SocketReceiveFromResult received = await fromUpstream.ReceiveFromAsync(
                        buffer,
                        new IPEndPoint(IPAddress.Any, 0),
                        stoppingToken
                    );

                    if (injectGarbage)
                        await udp.SendToAsync(
                            RandomNumberGenerator.GetBytes(50),
                            client,
                            stoppingToken
                        );

                    await udp.SendToAsync(
                        buffer.AsMemory(0, received.ReceivedBytes),
                        client,
                        stoppingToken
                    );
                }
            }
            catch (Exception exception)
                when (exception is OperationCanceledException or ObjectDisposedException) { }
        }

        /// <summary>
        ///     This method accepts client connections and counts them.
        /// </summary>
        private async Task TcpLoopAsync(CancellationToken stoppingToken)
        {
            try
            {
                while (true)
                {
                    TcpClient client = await tcp.AcceptTcpClientAsync(stoppingToken);
                    Interlocked.Increment(ref acceptedTcpConnections);
                    tasks.Add(Task.Run(() => PipeAsync(client, stoppingToken)));
                }
            }
            catch (Exception exception)
                when (exception is OperationCanceledException or ObjectDisposedException) { }
        }

        /// <summary>
        ///     This method copies bytes both ways between a client and the real TCP listener.
        /// </summary>
        private async Task PipeAsync(TcpClient client, CancellationToken stoppingToken)
        {
            try
            {
                using TcpClient toUpstream = new();
                await toUpstream.ConnectAsync(upstream, stoppingToken);
                Task one = client.GetStream().CopyToAsync(toUpstream.GetStream(), stoppingToken);
                Task two = toUpstream.GetStream().CopyToAsync(client.GetStream(), stoppingToken);
                await Task.WhenAny(one, two);
            }
            catch (Exception exception)
                when (exception is OperationCanceledException or IOException or SocketException) { }
            finally
            {
                client.Dispose();
            }
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await stop.CancelAsync();
            tcp.Stop();
            udp.Dispose();

            foreach (Socket socket in sessions.Values)
                socket.Dispose();

            await Task.WhenAll(tasks.ToArray()).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    /// <summary>
    ///     This class runs the real certificate store, rotation service, pipeline, and one
    ///     <see cref="DnsCryptUdpListener" /> and <see cref="DnsCryptTcpListener" /> on the same
    ///     loopback port, behind a <see cref="LoopbackRelay" />, and builds the stamp definition a
    ///     client uses to reach the relay.
    /// </summary>
    private sealed class DnsCryptHarness : IAsyncDisposable
    {
        private readonly string statePath = Path.Combine(
            Path.GetTempPath(),
            $"forwarding-dnscrypt-test-{Guid.NewGuid():N}.json"
        );
        private DnsCryptCertificateRotationService rotationService;
        private DnsQueryProcessingPipeline pipeline;
        private DnsCryptUdpListener udpListener;
        private DnsCryptTcpListener tcpListener;

        /// <summary>
        ///     This property contains the relay the client connects to.
        /// </summary>
        public LoopbackRelay Relay { get; private set; }

        /// <summary>
        ///     This property contains the dispatcher behind the listeners.
        /// </summary>
        public RecordingDispatcher Dispatcher { get; private set; }

        /// <summary>
        ///     This property contains the upstream definition that points at the relay.
        /// </summary>
        public UpstreamForwarderDefinition Definition { get; private set; }

        /// <summary>
        ///     This method starts everything.
        /// </summary>
        /// <param name="answerCount">How many A records every answer carries.</param>
        /// <param name="garbageBeforeUdpReplies">Passed to the relay.</param>
        public static async Task<DnsCryptHarness> StartAsync(
            int answerCount,
            bool garbageBeforeUdpReplies
        )
        {
            DnsCryptHarness harness = new();
            await harness.InitializeAsync(answerCount, garbageBeforeUdpReplies);
            return harness;
        }

        /// <summary>
        ///     This method binds the listeners on one shared port, then starts the relay and builds the
        ///     definition.
        /// </summary>
        private async Task InitializeAsync(int answerCount, bool garbageBeforeUdpReplies)
        {
            Dispatcher = new RecordingDispatcher(answerCount);
            DnsCryptOptions options = null;
            DnsCryptUdpListener udp = null;
            DnsCryptTcpListener tcp = null;
            FileDnsCryptCertificateStore store = null;

            // The listeners' UDP and TCP sockets must share one port; listener bind failures are
            // logged, not thrown, so IsListening tells whether both got it.
            for (int attempt = 0; ; attempt++)
            {
                options = new DnsCryptOptions
                {
                    Enable = true,
                    Port = GetEphemeralUdpPort(),
                    ProviderName = "test.example",
                    CertificateStateFilePath = statePath,
                    EnabledEsVersions = [DnsCryptEsVersion.XChaCha20Poly1305],
                    CertificateRotationInterval = TimeSpan.FromMinutes(10),
                    CertificateValidityDuration = TimeSpan.FromMinutes(20),
                    CertificateClockSkewTolerance = TimeSpan.Zero,
                };
                IOptionsMonitor<DnsCryptOptions> optionsMonitor =
                    new StaticOptionsMonitor<DnsCryptOptions>(options);

                store ??= new FileDnsCryptCertificateStore(
                    Options.Create(options),
                    NullLogger<FileDnsCryptCertificateStore>.Instance
                );
                rotationService ??= new DnsCryptCertificateRotationService(
                    store,
                    optionsMonitor,
                    NullLogger<DnsCryptCertificateRotationService>.Instance
                );

                if (pipeline is null)
                {
                    await rotationService.StartAsync(CancellationToken.None);

                    for (int wait = 0; wait < 40 && store.Entries.Count < 1; wait++)
                        await Task.Delay(50);

                    Assert.NotEmpty(store.Entries);

                    pipeline = new DnsQueryProcessingPipeline(
                        Dispatcher,
                        new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                            new DnsQueryProcessingPipelineOptions()
                        ),
                        NullLogger<DnsQueryProcessingPipeline>.Instance
                    );
                    await pipeline.StartAsync(CancellationToken.None);
                }

                udp = new DnsCryptUdpListener(
                    pipeline,
                    store,
                    optionsMonitor,
                    NullLogger<DnsCryptUdpListener>.Instance
                );
                tcp = new DnsCryptTcpListener(
                    pipeline,
                    store,
                    optionsMonitor,
                    NullLogger<DnsCryptTcpListener>.Instance
                );
                await udp.StartAsync(CancellationToken.None);
                await tcp.StartAsync(CancellationToken.None);

                // The hosted services bind on their own task, so wait for them to report.
                for (int wait = 0; wait < 40 && !(udp.IsListening && tcp.IsListening); wait++)
                    await Task.Delay(25);

                if ((udp.IsListening && tcp.IsListening) || attempt >= 10)
                    break;

                await udp.StopAsync(CancellationToken.None);
                await tcp.StopAsync(CancellationToken.None);
            }

            udpListener = udp;
            tcpListener = tcp;
            Assert.True(udp.IsListening && tcp.IsListening);

            Relay = new LoopbackRelay(
                new IPEndPoint(IPAddress.Loopback, options.Port),
                garbageBeforeUdpReplies
            );
            Definition = DnsCryptStampParser.Parse(
                BuildStamp(
                    $"127.0.0.1:{Relay.Port}",
                    store.ProviderKeyPair.PublicKeyWireFormat,
                    "2.dnscrypt-cert.test.example"
                )
            );
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (Relay is not null)
                await Relay.DisposeAsync();

            if (udpListener is not null)
                await udpListener.StopAsync(CancellationToken.None);

            if (tcpListener is not null)
                await tcpListener.StopAsync(CancellationToken.None);

            if (pipeline is not null)
                await pipeline.StopAsync(CancellationToken.None);

            if (rotationService is not null)
                await rotationService.StopAsync(CancellationToken.None);

            if (File.Exists(statePath))
                File.Delete(statePath);
        }
    }

    /// <summary>
    ///     This method builds an A query for forwarded.example with the given ID.
    /// </summary>
    private static DnsMessage BuildQuery(ushort id)
    {
        DnsMessage query = new()
        {
            Id = id,
            Opcode = DnsOpcodeEnum.Query,
            RecursionDesired = true,
        };
        query.Questions.Add(
            new DnsQuestion(
                DnsName.Parse("forwarded.example."),
                DnsResourceRecordTypeEnum.A,
                DnsClassEnum.Internet
            )
        );
        return query;
    }

    [Fact]
    public async Task ForwardAsync_ForAnAnswerTooBigForUdp_RetriesOverTcpWithANewIdAndKeepsTheCallersId()
    {
        await using DnsCryptHarness harness = await DnsCryptHarness.StartAsync(40, false);
        DnsCryptForwarderClient client = new();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));

        DnsMessage response = await client.ForwardAsync(
            harness.Definition,
            BuildQuery(4242),
            timeout.Token
        );

        Assert.False(response.IsTruncated);
        Assert.Equal(40, response.Answers.Count);
        Assert.Equal(4242, response.Id);
        Assert.Equal(1, harness.Relay.AcceptedTcpConnections);

        ushort[] ids = harness.Dispatcher.QueryIds.ToArray();
        Assert.Equal(2, ids.Length);
        Assert.NotEqual(ids[0], ids[1]);
    }

    [Fact]
    public async Task ForwardAsync_WithGarbageDatagramsBeforeTheReply_ReturnsTheRealReply()
    {
        await using DnsCryptHarness harness = await DnsCryptHarness.StartAsync(1, true);
        DnsCryptForwarderClient client = new();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));

        DnsMessage response = await client.ForwardAsync(
            harness.Definition,
            BuildQuery(77),
            timeout.Token
        );

        Assert.Single(response.Answers);
        Assert.Equal(77, response.Id);
    }

    [Fact]
    public async Task ForwardAsync_ForASmallAnswer_MakesOneUdpExchangeAndNoTcpConnection()
    {
        await using DnsCryptHarness harness = await DnsCryptHarness.StartAsync(1, false);
        DnsCryptForwarderClient client = new();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));

        DnsMessage response = await client.ForwardAsync(
            harness.Definition,
            BuildQuery(5),
            timeout.Token
        );

        Assert.Single(response.Answers);
        Assert.Equal(0, harness.Relay.AcceptedTcpConnections);
        Assert.Single(harness.Dispatcher.QueryIds);
    }

    [Fact]
    public void Parse_RejectsAStampWithTheWrongProtocolIdentifier()
    {
        ArrayBufferWriter<byte> buffer = new();
        buffer.GetSpan(1)[0] = 0x02;
        buffer.Advance(1);

        string stamp = "sdns://" + Base64Url.EncodeToString(buffer.WrittenSpan);

        Assert.Throws<FormatException>(() => DnsCryptStampParser.Parse(stamp));
    }
}
