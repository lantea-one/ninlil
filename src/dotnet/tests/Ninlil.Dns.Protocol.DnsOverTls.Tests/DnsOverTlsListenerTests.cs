using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Protocol.DnsOverTls.ProxyProtocol;
using Ninlil.Dns.Protocol.DnsOverTls.Tests.Fakes;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Ninlil.Dns.Server.Core.Certificates;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverTls.Tests;

/// <summary>
///     This class tests the DNS-over-TLS transport (RFC 7858) over real TCP sockets and TLS
///     handshakes, against the self-signed certificate the certificate provider generates when
///     none is configured. It covers framing, pipelining, zone transfers, allowed protocols, the
///     PROXY protocol and listen addresses. The resolver is a fake that answers every query, and
///     the zone transfer service is a fake that yields two messages; resolution itself is covered
///     by the resolver projects' tests.
/// </summary>
public sealed class DnsOverTlsListenerTests : IAsyncLifetime
{
    private static readonly int TestPort = GetFreePort();
    private static readonly IPAddress ExpectedAnswer = IPAddress.Parse("192.0.2.123");

    private DnsQueryProcessingPipeline _pipeline;
    private DnsOverTlsListener _listener;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        DnsQueryDispatcher dispatcher = new(
            [new AlwaysAnswerAResolver(ExpectedAnswer)],
            new NoOpEventPublisher()
        );

        _pipeline = new DnsQueryProcessingPipeline(
            dispatcher,
            new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                new DnsQueryProcessingPipelineOptions()
            ),
            NullLogger<DnsQueryProcessingPipeline>.Instance
        );
        await _pipeline.StartAsync(CancellationToken.None);

        DnsOverTlsOptions options = new()
        {
            Enable = true,
            Port = TestPort,
            IdleTimeout = TimeSpan.FromMilliseconds(400),
        };

        _listener = new DnsOverTlsListener(
            _pipeline,
            BuildScopeFactory(),
            new StaticOptionsMonitor<DnsOverTlsOptions>(options),
            NullLogger<DnsOverTlsListener>.Instance
        );
        await _listener.StartAsync(CancellationToken.None);

        await WaitUntilListeningAsync(_listener);
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        await _listener.StopAsync(CancellationToken.None);
        await _pipeline.StopAsync(CancellationToken.None);
        await _pipeline.DisposeAsync();
    }

    private static async Task<SslStream> ConnectAsync()
    {
        TcpClient client = new();
        await client.ConnectAsync(IPAddress.Loopback, TestPort);

        SslStream sslStream = new(
            client.GetStream(),
            leaveInnerStreamOpen: false,
            (_, _, _, _) => true
        );
        await sslStream.AuthenticateAsClientAsync("localhost");
        return sslStream;
    }

    private static async Task WriteQueryAsync(Stream stream, DnsMessage query)
    {
        byte[] messageBytes = DnsMessageWriter.Write(query);
        byte[] lengthPrefix = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(lengthPrefix, (ushort)messageBytes.Length);

        await stream.WriteAsync(lengthPrefix);
        await stream.WriteAsync(messageBytes);
    }

    private static async Task<DnsMessage> ReadResponseAsync(Stream stream)
    {
        byte[] lengthPrefix = new byte[2];
        await ReadExactAsync(stream, lengthPrefix);

        byte[] messageBytes = new byte[BinaryPrimitives.ReadUInt16BigEndian(lengthPrefix)];
        await ReadExactAsync(stream, messageBytes);

        return DnsMessageReader.Read(messageBytes, out _);
    }

    /// <summary>
    ///     This method builds an <see cref="IServiceScopeFactory" /> that resolves
    ///     <see cref="IZoneTransferService" /> to a <see cref="FakeZoneTransferService" />. The
    ///     listener's AXFR/IXFR branch needs a scope factory, but testing the transport side of a
    ///     transfer does not need the zone cache, TSIG and storage of
    ///     <c>Ninlil.Dns.Server.Authoritative</c>.
    /// </summary>
    private static IServiceScopeFactory BuildScopeFactory() =>
        new ServiceCollection()
            .AddScoped<IZoneTransferService, FakeZoneTransferService>()
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

    [Fact]
    public async Task AnAxfrQuery_IsAnsweredAsARealMultiMessageSequenceOverTheSameTlsConnection()
    {
        using SslStream stream = await ConnectAsync();

        DnsMessage query = new()
        {
            Id = 555,
            Opcode = DnsOpcodeEnum.Query,
            Questions =
            {
                new DnsQuestion(
                    DnsName.Parse("axfr-over-tls.example."),
                    DnsResourceRecordTypeEnum.Axfr,
                    DnsClassEnum.Internet
                ),
            },
        };
        await WriteQueryAsync(stream, query);

        // FakeZoneTransferService (registered in BuildScopeFactory) yields two messages for any
        // AXFR query; both must arrive on this TLS connection, in order, which a single
        // query/response round trip would not produce.
        DnsMessage first = await ReadResponseAsync(stream);
        DnsMessage second = await ReadResponseAsync(stream);

        Assert.Equal((ushort)1, first.Id);
        Assert.Equal((ushort)2, second.Id);
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer)
    {
        int totalRead = 0;

        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(totalRead));

            if (read == 0)
                throw new IOException("The connection closed before the expected bytes arrived.");

            totalRead += read;
        }
    }

    private static DnsMessage BuildQuery(ushort id, string name) =>
        new()
        {
            Id = id,
            Opcode = DnsOpcodeEnum.Query,
            RecursionDesired = true,
            Questions =
            {
                new DnsQuestion(
                    DnsName.Parse(name),
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet
                ),
            },
        };

    [Fact]
    public async Task ARealTlsHandshakeAndQueryResponseRoundTripsCorrectly()
    {
        using SslStream stream = await ConnectAsync();

        await WriteQueryAsync(stream, BuildQuery(1, "dot-test.example."));
        DnsMessage response = await ReadResponseAsync(stream);

        Assert.Equal(1, response.Id);
        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        Assert.Single(response.Answers);
        Assert.Equal(ExpectedAnswer, new IPAddress(response.Answers[0].Data));
    }

    [Fact]
    public async Task TwoPipelinedQueriesOnOneConnectionAreBothAnsweredCorrectly()
    {
        using SslStream stream = await ConnectAsync();

        await WriteQueryAsync(stream, BuildQuery(10, "first.example."));
        await WriteQueryAsync(stream, BuildQuery(11, "second.example."));

        DnsMessage firstResponse = await ReadResponseAsync(stream);
        DnsMessage secondResponse = await ReadResponseAsync(stream);

        // Pipelined queries (RFC 7766 §6.2.1) are answered by a shared worker pool, so either may
        // finish first; only the set of IDs is guaranteed, not the order.
        Assert.Equal(
            new HashSet<ushort> { 10, 11 },
            new HashSet<ushort> { firstResponse.Id, secondResponse.Id }
        );
    }

    [Fact]
    public async Task TheServerPresentsTheSelfSignedCertificateItGeneratedItself()
    {
        using TcpClient client = new();
        await client.ConnectAsync(IPAddress.Loopback, TestPort);

        X509Certificate2 remoteCertificate = null;
        using SslStream sslStream = new(
            client.GetStream(),
            leaveInnerStreamOpen: false,
            (_, certificate, _, _) =>
            {
                remoteCertificate = certificate is null ? null : new X509Certificate2(certificate);
                return true;
            }
        );

        await sslStream.AuthenticateAsClientAsync("localhost");

        Assert.NotNull(remoteCertificate);
        Assert.Contains("localhost", remoteCertificate.SubjectName.Name);
    }

    [Fact]
    public async Task RestrictingAllowedProtocolsToTls12AndTls13StillHandshakesSuccessfully()
    {
        int restrictedPort = GetFreePort();

        DnsQueryDispatcher dispatcher = new(
            [new AlwaysAnswerAResolver(ExpectedAnswer)],
            new NoOpEventPublisher()
        );
        DnsQueryProcessingPipeline pipeline = new(
            dispatcher,
            new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                new DnsQueryProcessingPipelineOptions()
            ),
            NullLogger<DnsQueryProcessingPipeline>.Instance
        );
        await pipeline.StartAsync(CancellationToken.None);

        DnsOverTlsListener listener = new(
            pipeline,
            BuildScopeFactory(),
            new StaticOptionsMonitor<DnsOverTlsOptions>(
                new DnsOverTlsOptions
                {
                    Enable = true,
                    Port = restrictedPort,
                    IdleTimeout = TimeSpan.FromMilliseconds(400),
                    Certificate = new TlsCertificateOptions
                    {
                        AllowedProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    },
                }
            ),
            NullLogger<DnsOverTlsListener>.Instance
        );

        try
        {
            await listener.StartAsync(CancellationToken.None);
            await WaitUntilListeningAsync(listener);

            using TcpClient client = new();
            await client.ConnectAsync(IPAddress.Loopback, restrictedPort);
            using SslStream stream = new(
                client.GetStream(),
                leaveInnerStreamOpen: false,
                (_, _, _, _) => true
            );
            await stream.AuthenticateAsClientAsync("localhost");

            await WriteQueryAsync(stream, BuildQuery(1, "dot-restricted-protocols.example."));
            DnsMessage response = await ReadResponseAsync(stream);

            Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
            Assert.True(stream.SslProtocol is SslProtocols.Tls12 or SslProtocols.Tls13);
        }
        finally
        {
            await listener.StopAsync(CancellationToken.None);
            await pipeline.StopAsync(CancellationToken.None);
            await pipeline.DisposeAsync();
        }
    }

    [Fact]
    public async Task AMalformedMessageIsDiscardedButTheConnectionAndFramingSurvive()
    {
        using SslStream stream = await ConnectAsync();

        // A length prefix of 3 and 3 garbage bytes, too short for the 12-byte header, so
        // DnsMessageReader.Read throws FormatException and the listener drops the message (it has
        // no reliable ID to answer with). Framing stays in sync, since the 3 bytes were read.
        byte[] malformedLengthPrefix = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(malformedLengthPrefix, 3);
        await stream.WriteAsync(malformedLengthPrefix);
        await stream.WriteAsync(new byte[] { 0xFF, 0xFF, 0xFF });

        await WriteQueryAsync(stream, BuildQuery(99, "after-malformed.example."));
        DnsMessage response = await ReadResponseAsync(stream);

        Assert.Equal(99, response.Id);
        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
    }

    private static readonly byte[] ProxyProtocolV2Signature =
    [
        0x0D,
        0x0A,
        0x0D,
        0x0A,
        0x00,
        0x0D,
        0x0A,
        0x51,
        0x55,
        0x49,
        0x54,
        0x0A,
    ];

    /// <summary>
    ///     This method builds a well-formed PROXY protocol v2 binary header (command PROXY, TCP over
    ///     IPv4) claiming <paramref name="claimedSource" /> as the client address.
    /// </summary>
    private static byte[] BuildV2ProxyHeader(IPAddress claimedSource)
    {
        using MemoryStream buffer = new();
        buffer.Write(ProxyProtocolV2Signature);
        buffer.WriteByte(0x21); // version 2, command PROXY.
        buffer.WriteByte(0x11); // TCP over IPv4.

        byte[] length = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, 12);
        buffer.Write(length);

        buffer.Write(claimedSource.GetAddressBytes());
        buffer.Write(IPAddress.Parse("192.0.2.1").GetAddressBytes());

        byte[] srcPort = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(srcPort, 56324);
        buffer.Write(srcPort);

        byte[] dstPort = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(dstPort, 853);
        buffer.Write(dstPort);

        return buffer.ToArray();
    }

    /// <summary>
    ///     This method starts a separate pipeline and listener, apart from the shared one in
    ///     <see cref="InitializeAsync" />, configured with <see cref="ProxyProtocolOptions" />. Each
    ///     PROXY protocol test needs its own listener, since the allowlist is what each varies.
    /// </summary>
    private static async Task<(
        DnsQueryProcessingPipeline Pipeline,
        DnsOverTlsListener Listener,
        RemoteAddressCapturingResolver Resolver
    )> StartProxyProtocolListenerAsync(int port, ProxyProtocolOptions proxyProtocolOptions)
    {
        RemoteAddressCapturingResolver resolver = new(ExpectedAnswer);
        DnsQueryDispatcher dispatcher = new([resolver], new NoOpEventPublisher());
        DnsQueryProcessingPipeline pipeline = new(
            dispatcher,
            new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                new DnsQueryProcessingPipelineOptions()
            ),
            NullLogger<DnsQueryProcessingPipeline>.Instance
        );
        await pipeline.StartAsync(CancellationToken.None);

        DnsOverTlsListener listener = new(
            pipeline,
            BuildScopeFactory(),
            new StaticOptionsMonitor<DnsOverTlsOptions>(
                new DnsOverTlsOptions
                {
                    Enable = true,
                    Port = port,
                    IdleTimeout = TimeSpan.FromMilliseconds(400),
                    ProxyProtocol = proxyProtocolOptions,
                }
            ),
            NullLogger<DnsOverTlsListener>.Instance
        );
        await listener.StartAsync(CancellationToken.None);
        await WaitUntilListeningAsync(listener);

        return (pipeline, listener, resolver);
    }

    private static async Task StopAsync(
        DnsQueryProcessingPipeline pipeline,
        DnsOverTlsListener listener
    )
    {
        await listener.StopAsync(CancellationToken.None);
        await pipeline.StopAsync(CancellationToken.None);
        await pipeline.DisposeAsync();
    }

    [Fact]
    public async Task AConnectionFromAKnownProxySource_WithAValidV1Header_UsesTheClaimedClientAddress()
    {
        int port = GetFreePort();
        IPAddress claimedAddress = IPAddress.Parse("203.0.113.9");

        (
            DnsQueryProcessingPipeline pipeline,
            DnsOverTlsListener listener,
            RemoteAddressCapturingResolver resolver
        ) = await StartProxyProtocolListenerAsync(
            port,
            new ProxyProtocolOptions { Enable = true, KnownProxies = ["127.0.0.1"] }
        );

        try
        {
            using TcpClient client = new();
            await client.ConnectAsync(IPAddress.Loopback, port);
            NetworkStream rawStream = client.GetStream();

            await rawStream.WriteAsync(
                Encoding.ASCII.GetBytes($"PROXY TCP4 {claimedAddress} 192.0.2.1 56324 853\r\n")
            );

            using SslStream sslStream = new(
                rawStream,
                leaveInnerStreamOpen: false,
                (_, _, _, _) => true
            );
            await sslStream.AuthenticateAsClientAsync("localhost");

            await WriteQueryAsync(sslStream, BuildQuery(1, "proxy-v1.example."));
            DnsMessage response = await ReadResponseAsync(sslStream);

            Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
            Assert.Equal(claimedAddress, resolver.LastRemoteAddress);
        }
        finally
        {
            await StopAsync(pipeline, listener);
        }
    }

    [Fact]
    public async Task AConnectionFromAKnownProxySource_WithAValidV2Header_UsesTheClaimedClientAddress()
    {
        int port = GetFreePort();
        IPAddress claimedAddress = IPAddress.Parse("203.0.113.42");

        (
            DnsQueryProcessingPipeline pipeline,
            DnsOverTlsListener listener,
            RemoteAddressCapturingResolver resolver
        ) = await StartProxyProtocolListenerAsync(
            port,
            new ProxyProtocolOptions { Enable = true, KnownProxies = ["127.0.0.1"] }
        );

        try
        {
            using TcpClient client = new();
            await client.ConnectAsync(IPAddress.Loopback, port);
            NetworkStream rawStream = client.GetStream();

            await rawStream.WriteAsync(BuildV2ProxyHeader(claimedAddress));

            using SslStream sslStream = new(
                rawStream,
                leaveInnerStreamOpen: false,
                (_, _, _, _) => true
            );
            await sslStream.AuthenticateAsClientAsync("localhost");

            await WriteQueryAsync(sslStream, BuildQuery(2, "proxy-v2.example."));
            DnsMessage response = await ReadResponseAsync(sslStream);

            Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
            Assert.Equal(claimedAddress, resolver.LastRemoteAddress);
        }
        finally
        {
            await StopAsync(pipeline, listener);
        }
    }

    [Fact]
    public async Task AConnectionFromAnUnknownSource_SendingAForgedProxyHeader_IsNeverInspectedForOne()
    {
        int port = GetFreePort();

        // The PROXY protocol is on, but only for a source that is not loopback, so a forged
        // header from this untrusted connection must not be read as one.
        (DnsQueryProcessingPipeline pipeline, DnsOverTlsListener listener, _) =
            await StartProxyProtocolListenerAsync(
                port,
                new ProxyProtocolOptions { Enable = true, KnownProxies = ["203.0.113.99"] }
            );

        try
        {
            using TcpClient client = new();
            await client.ConnectAsync(IPAddress.Loopback, port);
            NetworkStream rawStream = client.GetStream();

            // A forged v1 header from an untrusted source is not read as a header; the bytes go
            // to the TLS handshake as a garbage ClientHello, which must fail.
            await rawStream.WriteAsync(
                Encoding.ASCII.GetBytes("PROXY TCP4 203.0.113.1 203.0.113.2 1 2\r\n")
            );

            using SslStream sslStream = new(
                rawStream,
                leaveInnerStreamOpen: false,
                (_, _, _, _) => true
            );
            await Assert.ThrowsAnyAsync<Exception>(() =>
                sslStream.AuthenticateAsClientAsync("localhost")
            );
        }
        finally
        {
            await StopAsync(pipeline, listener);
        }
    }

    [Fact]
    public async Task AConnectionFromAKnownProxySource_WithAMalformedHeader_IsRejected()
    {
        int port = GetFreePort();

        (DnsQueryProcessingPipeline pipeline, DnsOverTlsListener listener, _) =
            await StartProxyProtocolListenerAsync(
                port,
                new ProxyProtocolOptions { Enable = true, KnownProxies = ["127.0.0.1"] }
            );

        try
        {
            using TcpClient client = new();
            await client.ConnectAsync(IPAddress.Loopback, port);
            NetworkStream rawStream = client.GetStream();

            // A trusted source sends a line that is not a PROXY protocol header, so the server
            // aborts the connection, as the specification requires, before the TLS handshake.
            await rawStream.WriteAsync(Encoding.ASCII.GetBytes("THIS IS NOT A PROXY HEADER\r\n"));

            using SslStream sslStream = new(
                rawStream,
                leaveInnerStreamOpen: false,
                (_, _, _, _) => true
            );
            await Assert.ThrowsAnyAsync<Exception>(() =>
                sslStream.AuthenticateAsClientAsync("localhost")
            );
        }
        finally
        {
            await StopAsync(pipeline, listener);
        }
    }

    /// <summary>
    ///     This method starts a separate pipeline and listener bound to the given listen addresses
    ///     on a free port, apart from the shared one in <see cref="InitializeAsync" />.
    /// </summary>
    private static async Task<(
        DnsQueryProcessingPipeline Pipeline,
        DnsOverTlsListener Listener,
        int Port
    )> StartListenAddressListenerAsync(string listenAddresses, ILogger<DnsOverTlsListener> logger)
    {
        int port = LocalAddresses.GetFreePort();
        DnsQueryProcessingPipeline pipeline = new(
            new DnsQueryDispatcher(
                [new AlwaysAnswerAResolver(ExpectedAnswer)],
                new NoOpEventPublisher()
            ),
            new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                new DnsQueryProcessingPipelineOptions()
            ),
            NullLogger<DnsQueryProcessingPipeline>.Instance
        );
        await pipeline.StartAsync(CancellationToken.None);

        DnsOverTlsListener listener = new(
            pipeline,
            BuildScopeFactory(),
            new StaticOptionsMonitor<DnsOverTlsOptions>(
                new DnsOverTlsOptions
                {
                    Enable = true,
                    Port = port,
                    ListenAddresses = listenAddresses,
                }
            ),
            logger
        );
        await listener.StartAsync(CancellationToken.None);
        await WaitUntilListeningAsync(listener);

        return (pipeline, listener, port);
    }

    /// <summary>
    ///     This method waits, for at most 10 seconds, until <paramref name="listener" /> is bound.
    ///     <c>ExecuteAsync</c> binds after <c>StartAsync</c> returns, and sets
    ///     <see cref="DnsOverTlsListener.IsListening" /> only once every address was tried and the
    ///     failed ones recorded, so it is the bind signal; a fixed sleep is not.
    /// </summary>
    private static async Task WaitUntilListeningAsync(DnsOverTlsListener listener)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));

        while (!listener.IsListening)
            await Task.Delay(10, timeout.Token);
    }

    /// <summary>
    ///     This method repeats <see cref="TryQueryAsync" /> until an answer arrives, for at most
    ///     15 seconds. It is for addresses the listener must serve: a refused connection there
    ///     means the listener is not ready yet, not that the test passed.
    /// </summary>
    /// <returns>The answer, or <see langword="null" /> when none arrived in time.</returns>
    private static async Task<DnsMessage> QueryUntilAnsweredAsync(IPAddress address, int port)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));

        while (!timeout.IsCancellationRequested)
        {
            DnsMessage response = await TryQueryAsync(address, port);

            if (response is not null)
                return response;

            try
            {
                await Task.Delay(50, timeout.Token);
            }
            catch (OperationCanceledException) { }
        }

        return null;
    }

    /// <summary>
    ///     This method runs one full TLS query against an address, returning <see langword="null" />
    ///     when the connection is refused.
    /// </summary>
    private static async Task<DnsMessage> TryQueryAsync(IPAddress address, int port)
    {
        using TcpClient client = new(address.AddressFamily);

        try
        {
            await client.ConnectAsync(address, port);
        }
        catch (SocketException)
        {
            return null;
        }

        using SslStream sslStream = new(
            client.GetStream(),
            leaveInnerStreamOpen: false,
            (_, _, _, _) => true
        );
        await sslStream.AuthenticateAsClientAsync("localhost");
        await WriteQueryAsync(sslStream, BuildQuery(99, "listen.example."));
        return await ReadResponseAsync(sslStream);
    }

    [Fact]
    public async Task ListenAddresses_LoopbackOnly_AnswersThereButNotOnAnotherLocalAddress()
    {
        IPAddress other = LocalAddresses.OtherLoopback();

        if (other is null)
            return;

        (DnsQueryProcessingPipeline pipeline, DnsOverTlsListener listener, int port) =
            await StartListenAddressListenerAsync(
                "127.0.0.1",
                NullLogger<DnsOverTlsListener>.Instance
            );

        try
        {
            DnsMessage response = await QueryUntilAnsweredAsync(IPAddress.Loopback, port);
            Assert.NotNull(response);
            Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);

            // A negative check is one attempt: "nothing answers" has no signal to wait for, and
            // the listener is already bound, so a refused connection is the answer.
            Assert.Null(await TryQueryAsync(other, port));
        }
        finally
        {
            await StopAsync(pipeline, listener);
        }
    }

    [Fact]
    public async Task ListenAddresses_ABadAddress_IsLoggedAndReportedWhileTheOthersAnswer()
    {
        IPAddress other = LocalAddresses.OtherLoopback();

        if (other is null)
            return;

        CapturingLogger<DnsOverTlsListener> logger = new();
        (DnsQueryProcessingPipeline pipeline, DnsOverTlsListener listener, int port) =
            await StartListenAddressListenerAsync(
                $"127.0.0.1, {LocalAddresses.Unassigned}, {other}",
                logger
            );

        try
        {
            Assert.True(listener.IsListening);
            Assert.NotNull(await QueryUntilAnsweredAsync(IPAddress.Loopback, port));
            Assert.NotNull(await QueryUntilAnsweredAsync(other, port));

            string failed = Assert.Single(listener.FailedListenAddresses);
            Assert.StartsWith("192.0.2.1 (", failed);
            Assert.Contains(
                logger.Errors,
                message => message.Contains("DNS-over-TLS") && message.Contains("192.0.2.1")
            );
        }
        finally
        {
            await StopAsync(pipeline, listener);
        }
    }

    /// <summary>
    ///     This resolver claims every query and answers it with one fixed A record, whatever the
    ///     question; resolution is not under test here.
    /// </summary>
    private sealed class AlwaysAnswerAResolver(IPAddress answer) : IDnsQueryResolver
    {
        public ValueTask<DnsQueryResolution> TryResolveAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        )
        {
            DnsMessage response = new()
            {
                Id = context.Request.Id,
                IsResponse = true,
                Opcode = context.Request.Opcode,
                IsAuthoritativeAnswer = true,
                RecursionDesired = context.Request.RecursionDesired,
                ResponseCode = DnsResponseCodeEnum.NoError,
                Questions = { context.Request.Questions[0] },
            };

            response.Answers.Add(
                new DnsResourceRecord(
                    context.Request.Questions[0].Name,
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet,
                    60,
                    answer.GetAddressBytes()
                )
            );

            return ValueTask.FromResult(DnsQueryResolution.Handled(response));
        }
    }

    /// <summary>
    ///     This resolver works like <see cref="AlwaysAnswerAResolver" /> but also records each
    ///     query's <see cref="DnsQueryContext.RemoteAddress" />, so a PROXY protocol test can check
    ///     that the claimed client address reached the resolver.
    /// </summary>
    private sealed class RemoteAddressCapturingResolver(IPAddress answer) : IDnsQueryResolver
    {
        /// <summary>
        ///     This property contains the most recent query's <see cref="DnsQueryContext.RemoteAddress" />.
        /// </summary>
        public IPAddress LastRemoteAddress { get; private set; }

        public ValueTask<DnsQueryResolution> TryResolveAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        )
        {
            LastRemoteAddress = context.RemoteAddress;

            DnsMessage response = new()
            {
                Id = context.Request.Id,
                IsResponse = true,
                Opcode = context.Request.Opcode,
                IsAuthoritativeAnswer = true,
                RecursionDesired = context.Request.RecursionDesired,
                ResponseCode = DnsResponseCodeEnum.NoError,
                Questions = { context.Request.Questions[0] },
            };

            response.Answers.Add(
                new DnsResourceRecord(
                    context.Request.Questions[0].Name,
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet,
                    60,
                    answer.GetAddressBytes()
                )
            );

            return ValueTask.FromResult(DnsQueryResolution.Handled(response));
        }
    }

    /// <summary>
    ///     This publisher discards every event; the event bus is not under test here.
    /// </summary>
    private sealed class NoOpEventPublisher : IDnsServerEventPublisher
    {
        public void Publish(IDnsServerEvent domainEvent) { }
    }

    /// <summary>
    ///     This class is a fake <see cref="IZoneTransferService" /> that yields two fixed messages
    ///     for any AXFR or IXFR query, enough to check that the listener streams every message back
    ///     over the same TLS connection, in order.
    /// </summary>
    private sealed class FakeZoneTransferService : IZoneTransferService
    {
        public async IAsyncEnumerable<DnsMessage> CreateAxfrResponseAsync(
            DnsQueryContext context,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
                CancellationToken stoppingToken = default
        )
        {
            yield return new DnsMessage { Id = 1, IsResponse = true };
            await Task.Yield();
            yield return new DnsMessage { Id = 2, IsResponse = true };
        }

        public IAsyncEnumerable<DnsMessage> CreateIxfrResponseAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        ) => CreateAxfrResponseAsync(context, stoppingToken);
    }

    /// <summary>
    ///     This method asks the operating system for a currently unused port, by binding to port 0
    ///     on loopback and reading back what was assigned. Tests run on shared machines with their
    ///     own DNS, DHCP and web services, and may run alongside another copy of this suite, so no
    ///     test listener uses a fixed or well-known port.
    /// </summary>
    /// <returns>A free TCP port number.</returns>
    private static int GetFreePort()
    {
        using System.Net.Sockets.TcpListener listener = new(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }
}
