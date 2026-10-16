using System.Buffers.Binary;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Protocol.DnsOverQuic.Tests.Fakes;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Ninlil.Dns.Server.Core.Certificates;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Resolution;
using Xunit.Abstractions;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverQuic.Tests;

/// <summary>
///     This class tests a running <see cref="DnsOverQuicListener" /> over loopback QUIC
///     connections: query round trips, concurrent streams, a second connection, zone transfers
///     and listen addresses. The query dispatcher and the zone transfer service are fakes.
///     <para>
///         QUIC needs <c>libmsquic</c> (and, on Linux, OpenSSL) installed on the host. Every test
///         that needs a connection checks <see cref="QuicListener.IsSupported" /> and
///         <see cref="QuicConnection.IsSupported" /> and returns early when they are false, so it
///         is reported as passed, not skipped (there is no runtime skip mechanism in these test
///         projects). This matches the listener, which logs and does not start in that case.
///     </para>
/// </summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
[SupportedOSPlatform("windows")]
public sealed class DnsOverQuicListenerTests : IAsyncLifetime
{
    private static readonly SslApplicationProtocol DoqProtocol = new("doq");

    private readonly ITestOutputHelper _output;
    private DnsQueryProcessingPipeline _pipeline;
    private DnsOverQuicListener _listener;
    private DnsOverQuicOptions _options;
    private string _certificateStatePath;

    public DnsOverQuicListenerTests(ITestOutputHelper output) => _output = output;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _certificateStatePath = Path.Combine(
            Path.GetTempPath(),
            $"doq-tests-{Guid.NewGuid():N}.pfx"
        );
        _options = new DnsOverQuicOptions
        {
            Enable = true,
            Port = GetFreeUdpPort(),
            Certificate = new TlsCertificateOptions
            {
                SelfSignedCertificateStatePath = _certificateStatePath,
                SelfSignedSubjectAlternativeNames = ["localhost"],
            },
        };

        _pipeline = new DnsQueryProcessingPipeline(
            new EchoingDispatcher(),
            new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                new DnsQueryProcessingPipelineOptions()
            ),
            NullLogger<DnsQueryProcessingPipeline>.Instance
        );
        await _pipeline.StartAsync(CancellationToken.None);

        _listener = new DnsOverQuicListener(
            _pipeline,
            BuildScopeFactory(),
            new StaticOptionsMonitor<DnsOverQuicOptions>(_options),
            NullLogger<DnsOverQuicListener>.Instance
        );
        await _listener.StartAsync(CancellationToken.None);

        // The listener calls QuicListener.ListenAsync on a background task; give it a moment to
        // bind before any test connects.
        await Task.Delay(TimeSpan.FromMilliseconds(250));
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_listener is not null)
            await _listener.StopAsync(CancellationToken.None);

        if (_pipeline is not null)
            await _pipeline.StopAsync(CancellationToken.None);

        if (_certificateStatePath is not null && File.Exists(_certificateStatePath))
            File.Delete(_certificateStatePath);
    }

    [Fact]
    public async Task AxfrQuery_IsAnsweredAsARealMultiMessageSequenceOnTheSameStream()
    {
        if (!IsQuicAvailable())
            return;

        await using QuicConnection connection = await ConnectAsync();
        await using QuicStream stream = await connection.OpenOutboundStreamAsync(
            QuicStreamType.Bidirectional
        );

        DnsMessage query = new()
        {
            Id = 0,
            Opcode = DnsOpcodeEnum.Query,
            Questions =
            {
                new DnsQuestion(
                    DnsName.Parse("axfr-over-quic.example."),
                    DnsResourceRecordTypeEnum.Axfr,
                    DnsClassEnum.Internet
                ),
            },
        };
        await SendAsync(stream, query);

        // FakeZoneTransferService (registered in InitializeAsync's scope factory) yields two
        // messages for any AXFR query; both must arrive on this stream before it completes.
        DnsMessage first = await ReceiveAsync(stream);
        DnsMessage second = await ReceiveAsync(stream);

        Assert.Equal((ushort)1, first.Id);
        Assert.Equal((ushort)2, second.Id);
    }

    [Fact]
    public void QuicSupportIsReported()
    {
        // Records, rather than asserts, whether QUIC is available; the tests below do nothing
        // (and pass) when it is not.
        _output.WriteLine(
            $"QuicListener.IsSupported = {QuicListener.IsSupported}, QuicConnection.IsSupported = {QuicConnection.IsSupported}"
        );
        Assert.True(true);
    }

    [Fact]
    public async Task QueryResponseRoundTrip_Succeeds()
    {
        if (!IsQuicAvailable())
            return;

        await using QuicConnection connection = await ConnectAsync();
        await using QuicStream stream = await connection.OpenOutboundStreamAsync(
            QuicStreamType.Bidirectional
        );

        DnsMessage query = BuildQuery(0, "example.com");
        await SendAsync(stream, query);
        DnsMessage response = await ReceiveAsync(stream);

        Assert.Equal(query.Id, response.Id);
        Assert.True(response.IsResponse);
        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        Assert.Single(response.Answers);
        Assert.Equal(DnsResourceRecordTypeEnum.A, response.Answers[0].Type);
    }

    [Fact]
    public async Task AQueryWithANonZeroMessageId_AbortsTheConnectionWithProtocolErrorAndGetsNoReply()
    {
        if (!IsQuicAvailable())
            return;

        await using QuicConnection connection = await ConnectAsync();
        await using QuicStream stream = await connection.OpenOutboundStreamAsync(
            QuicStreamType.Bidirectional
        );
        await SendAsync(stream, BuildQuery(7, "non-zero-id.example.com"));

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        QuicException aborted = await Assert.ThrowsAsync<QuicException>(async () =>
            await stream.ReadExactlyAsync(new byte[2], timeout.Token)
        );

        // RFC 9250 §4.3.3: the whole connection ends with DOQ_PROTOCOL_ERROR (0x2).
        Assert.Equal(QuicError.ConnectionAborted, aborted.QuicError);
        Assert.Equal(2, aborted.ApplicationErrorCode);
    }

    [Fact]
    public async Task AZoneTransferQueryWithANonZeroMessageId_AbortsTheConnectionAndIsNotTransferred()
    {
        if (!IsQuicAvailable())
            return;

        await using QuicConnection connection = await ConnectAsync();
        await using QuicStream stream = await connection.OpenOutboundStreamAsync(
            QuicStreamType.Bidirectional
        );
        DnsMessage query = BuildQuery(7, "axfr-non-zero-id.example.com");
        query.Questions[0] = new DnsQuestion(
            query.Questions[0].Name,
            DnsResourceRecordTypeEnum.Axfr,
            DnsClassEnum.Internet
        );
        await SendAsync(stream, query);

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        QuicException aborted = await Assert.ThrowsAsync<QuicException>(async () =>
            await stream.ReadExactlyAsync(new byte[2], timeout.Token)
        );

        Assert.Equal(QuicError.ConnectionAborted, aborted.QuicError);
        Assert.Equal(2, aborted.ApplicationErrorCode);
    }

    [Fact]
    public async Task AQueryWithMessageIdZero_IsAnsweredWithMessageIdZero()
    {
        if (!IsQuicAvailable())
            return;

        await using QuicConnection connection = await ConnectAsync();
        await using QuicStream stream = await connection.OpenOutboundStreamAsync(
            QuicStreamType.Bidirectional
        );
        await SendAsync(stream, BuildQuery(0, "zero-id.example.com"));
        DnsMessage response = await ReceiveAsync(stream);

        Assert.Equal((ushort)0, response.Id);
        Assert.Single(response.Answers);
    }

    [Fact]
    public async Task SecondIndependentConnection_AfterFirstCloses_IsStillServed()
    {
        if (!IsQuicAvailable())
            return;

        await using (QuicConnection first = await ConnectAsync())
        {
            await using QuicStream stream = await first.OpenOutboundStreamAsync(
                QuicStreamType.Bidirectional
            );
            await SendAsync(stream, BuildQuery(0, "first.example.com"));
            await ReceiveAsync(stream);
        }

        await using QuicConnection second = await ConnectAsync();
        await using QuicStream secondStream = await second.OpenOutboundStreamAsync(
            QuicStreamType.Bidirectional
        );
        await SendAsync(secondStream, BuildQuery(0, "second.example.com"));
        DnsMessage response = await ReceiveAsync(secondStream);

        Assert.Equal(DnsName.Parse("second.example.com"), response.Questions[0].Name);
    }

    [Fact]
    public async Task MultipleConcurrentStreamsOnOneConnection_AreAllAnswered()
    {
        if (!IsQuicAvailable())
            return;

        await using QuicConnection connection = await ConnectAsync();

        // Every DoQ message has ID 0, so the streams are told apart by their question name.
        async Task<DnsMessage> QueryAsync(int number)
        {
            await using QuicStream stream = await connection.OpenOutboundStreamAsync(
                QuicStreamType.Bidirectional
            );
            await SendAsync(stream, BuildQuery(0, $"q{number}.example.com"));
            return await ReceiveAsync(stream);
        }

        DnsMessage[] responses = await Task.WhenAll(QueryAsync(10), QueryAsync(11), QueryAsync(12));

        Assert.Equal(
            ["q10.example.com.", "q11.example.com.", "q12.example.com."],
            responses.Select(response => response.Questions[0].Name.ToString()).Order()
        );
    }

    [Fact]
    public async Task AClientWithTheWrongAlpn_DoesNotStopTheListenerServingTheNextClient()
    {
        if (!IsQuicAvailable())
            return;

        // The handshake fails on the server (no shared application protocol). That failure must
        // end only this connection, not the accept loop.
        try
        {
            await using QuicConnection wrong = await QuicConnection.ConnectAsync(
                new QuicClientConnectionOptions
                {
                    RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, _options.Port),
                    ClientAuthenticationOptions = new SslClientAuthenticationOptions
                    {
                        ApplicationProtocols = [new SslApplicationProtocol("h3")],
                        TargetHost = "localhost",
                        RemoteCertificateValidationCallback = (_, _, _, _) => true,
                    },
                    DefaultCloseErrorCode = 0,
                    DefaultStreamErrorCode = 0,
                }
            );
        }
        catch (Exception exception)
            when (exception
                    is QuicException
                        or System.Security.Authentication.AuthenticationException
            ) { }

        DnsMessage response = await TryQueryAsync(IPAddress.Loopback, _options.Port);

        Assert.NotNull(response);
        Assert.True(_listener.IsListening);
    }

    [Fact]
    public async Task TheAcceptLoop_SurvivesFailedHandshakesAndKeepsAccepting()
    {
        // No QUIC peer is involved: the accept call is a delegate that fails the way
        // QuicListener.AcceptConnectionAsync does for a connection whose handshake failed.
        using CancellationTokenSource stop = new();
        int calls = 0;
        TaskCompletionSource reachedThirdCall = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        ValueTask<QuicConnection> Accept(CancellationToken token)
        {
            switch (Interlocked.Increment(ref calls))
            {
                case 1:
                    throw new QuicException(QuicError.ConnectionAborted, 120, "handshake failed");
                case 2:
                    throw new System.Security.Authentication.AuthenticationException(
                        "handshake failed"
                    );
                default:
                    reachedThirdCall.TrySetResult();
                    return new ValueTask<QuicConnection>(
                        Task.Delay(Timeout.Infinite, token).ContinueWith<QuicConnection>(_ => null)
                    );
            }
        }

        Task loop = _listener.AcceptLoopAsync(Accept, stop.Token);
        Task finished = await Task.WhenAny(reachedThirdCall.Task, loop);

        Assert.Same(reachedThirdCall.Task, finished);

        await stop.CancelAsync();
        await loop.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task AConnectionOverMaxConcurrentConnections_IsClosedWithExcessiveLoad()
    {
        if (!IsQuicAvailable())
            return;

        (DnsQueryProcessingPipeline pipeline, DnsOverQuicListener listener, int port) =
            await StartListenAddressListenerAsync(
                string.Empty,
                NullLogger<DnsOverQuicListener>.Instance,
                configured => configured.MaxConcurrentConnections = 1
            );

        try
        {
            await using QuicConnection first = await ConnectToAsync(port);
            await using (QuicStream firstStream = await OpenAnsweredStreamAsync(first))
            {
                // The first connection is now registered on the server.
            }

            await using QuicConnection second = await ConnectToAsync(port);
            QuicException closed = await Assert.ThrowsAsync<QuicException>(async () =>
            {
                await using QuicStream stream = await second.OpenOutboundStreamAsync(
                    QuicStreamType.Bidirectional
                );
                await SendAsync(stream, BuildQuery(0, "over-the-limit.example.com"));
                await ReceiveAsync(stream);
            });

            Assert.Equal(QuicError.ConnectionAborted, closed.QuicError);
            Assert.Equal(4, closed.ApplicationErrorCode);

            // Closing the first connection frees its slot for a new one.
            await first.CloseAsync(0);
            Assert.NotNull(await QueryEventuallyAsync(port));
        }
        finally
        {
            await listener.StopAsync(CancellationToken.None);
            await pipeline.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task AStreamThatNeverSendsItsQuery_IsResetAfterTheIdleTimeout()
    {
        if (!IsQuicAvailable())
            return;

        (DnsQueryProcessingPipeline pipeline, DnsOverQuicListener listener, int port) =
            await StartListenAddressListenerAsync(
                string.Empty,
                NullLogger<DnsOverQuicListener>.Instance,
                configured => configured.IdleTimeout = TimeSpan.FromSeconds(1)
            );

        try
        {
            // Keep-alives stop the QUIC connection idling out first, so it is the stream's read
            // deadline that ends the stream.
            await using QuicConnection connection = await ConnectToAsync(
                port,
                keepAlive: TimeSpan.FromMilliseconds(200)
            );
            await using QuicStream stream = await connection.OpenOutboundStreamAsync(
                QuicStreamType.Bidirectional
            );
            // A stream is only announced to the server once it carries data; send the length
            // prefix of a message that never follows.
            await stream.WriteAsync(new byte[] { 0, 40 });

            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
            QuicException reset = await Assert.ThrowsAsync<QuicException>(async () =>
                await stream.ReadExactlyAsync(new byte[1], timeout.Token)
            );

            Assert.Equal(QuicError.StreamAborted, reset.QuicError);
            Assert.Equal(3, reset.ApplicationErrorCode);
        }
        finally
        {
            await listener.StopAsync(CancellationToken.None);
            await pipeline.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task AQueryThePipelineDrops_ResetsItsStreamWithRequestCancelled()
    {
        if (!IsQuicAvailable())
            return;

        await using QuicConnection connection = await ConnectAsync();
        await using QuicStream stream = await connection.OpenOutboundStreamAsync(
            QuicStreamType.Bidirectional
        );
        await SendAsync(stream, BuildQuery(0, "drop.example.com"));

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        QuicException reset = await Assert.ThrowsAsync<QuicException>(async () =>
            await stream.ReadExactlyAsync(new byte[1], timeout.Token)
        );

        Assert.Equal(QuicError.StreamAborted, reset.QuicError);
        Assert.Equal(3, reset.ApplicationErrorCode);

        // The connection is unaffected: a following query on a new stream is answered.
        await using QuicStream next = await OpenAnsweredStreamAsync(connection);
    }

    [Fact]
    public async Task ListenAddresses_LoopbackOnly_AnswersThereButNotOnAnotherLocalAddress()
    {
        IPAddress other = LocalAddresses.OtherLoopback();

        if (!IsQuicAvailable() || other is null)
            return;

        (DnsQueryProcessingPipeline pipeline, DnsOverQuicListener listener, int port) =
            await StartListenAddressListenerAsync(
                "127.0.0.1",
                NullLogger<DnsOverQuicListener>.Instance
            );

        try
        {
            DnsMessage response = await TryQueryAsync(IPAddress.Loopback, port);
            Assert.NotNull(response);
            Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);

            Assert.Null(await TryQueryAsync(other, port));
        }
        finally
        {
            await listener.StopAsync(CancellationToken.None);
            await pipeline.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ListenAddresses_ABadAddress_IsLoggedAndReportedWhileTheOthersAnswer()
    {
        IPAddress other = LocalAddresses.OtherLoopback();

        if (!IsQuicAvailable() || other is null)
            return;

        CapturingLogger<DnsOverQuicListener> logger = new();
        (DnsQueryProcessingPipeline pipeline, DnsOverQuicListener listener, int port) =
            await StartListenAddressListenerAsync(
                $"127.0.0.1, {LocalAddresses.Unassigned}, {other}",
                logger
            );

        try
        {
            Assert.True(listener.IsListening);
            Assert.NotNull(await TryQueryAsync(IPAddress.Loopback, port));
            Assert.NotNull(await TryQueryAsync(other, port));

            string failed = Assert.Single(listener.FailedListenAddresses);
            Assert.StartsWith("192.0.2.1 (", failed);
            Assert.Contains(
                logger.Errors,
                message => message.Contains("DNS-over-QUIC") && message.Contains("192.0.2.1")
            );
        }
        finally
        {
            await listener.StopAsync(CancellationToken.None);
            await pipeline.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    ///     This method starts a separate pipeline and listener bound to the given listen addresses
    ///     on a free port, apart from the shared one in <see cref="InitializeAsync" /> (which
    ///     listens on every address).
    /// </summary>
    private async Task<(
        DnsQueryProcessingPipeline Pipeline,
        DnsOverQuicListener Listener,
        int Port
    )> StartListenAddressListenerAsync(
        string listenAddresses,
        ILogger<DnsOverQuicListener> logger,
        Action<DnsOverQuicOptions> configure = null
    )
    {
        int port = LocalAddresses.GetFreePort();
        DnsQueryProcessingPipeline pipeline = new(
            new EchoingDispatcher(),
            new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                new DnsQueryProcessingPipelineOptions()
            ),
            NullLogger<DnsQueryProcessingPipeline>.Instance
        );
        await pipeline.StartAsync(CancellationToken.None);

        DnsOverQuicOptions listenerOptions = new()
        {
            Enable = true,
            Port = port,
            ListenAddresses = listenAddresses,
            // Reuse the shared self-signed certificate file, so each listener does not
            // generate its own.
            Certificate = _options.Certificate,
        };
        configure?.Invoke(listenerOptions);

        DnsOverQuicListener listener = new(
            pipeline,
            BuildScopeFactory(),
            new StaticOptionsMonitor<DnsOverQuicOptions>(listenerOptions),
            logger
        );
        await listener.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(250));

        return (pipeline, listener, port);
    }

    /// <summary>
    ///     This method runs one full DoQ query against an address, returning
    ///     <see langword="null" /> when nothing answers there. QUIC runs over UDP, so an address
    ///     with no listener is never "refused" the way TCP would be. Depending on the platform the
    ///     attempt either times out (given two seconds) or, because MsQuic shares one underlying
    ///     socket per port, reaches it and has its handshake rejected with an ALPN error. Either
    ///     way nothing is served there, which is what counts.
    /// </summary>
    private static async Task<DnsMessage> TryQueryAsync(IPAddress address, int port)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(2));

        try
        {
            await using QuicConnection connection = await QuicConnection.ConnectAsync(
                new QuicClientConnectionOptions
                {
                    RemoteEndPoint = new IPEndPoint(address, port),
                    ClientAuthenticationOptions = new SslClientAuthenticationOptions
                    {
                        ApplicationProtocols = [DoqProtocol],
                        TargetHost = "localhost",
                        RemoteCertificateValidationCallback = (_, _, _, _) => true,
                    },
                    DefaultCloseErrorCode = 0,
                    DefaultStreamErrorCode = 0,
                },
                timeout.Token
            );
            await using QuicStream stream = await connection.OpenOutboundStreamAsync(
                QuicStreamType.Bidirectional,
                timeout.Token
            );
            await SendAsync(stream, BuildQuery(0, "listen.example.com"));
            return await ReceiveAsync(stream).WaitAsync(timeout.Token);
        }
        catch (Exception exception)
            when (exception
                    is QuicException
                        or OperationCanceledException
                        or SocketException
                        or System.Security.Authentication.AuthenticationException
            )
        {
            return null;
        }
    }

    /// <summary>
    ///     This method reports whether QUIC works in this environment. When it does not, it writes
    ///     a note to the test output, so a test that returns early does not pass without
    ///     explanation.
    /// </summary>
    private bool IsQuicAvailable()
    {
        if (QuicListener.IsSupported && QuicConnection.IsSupported)
            return true;

        _output.WriteLine(
            "Skipping: QUIC is not supported in this environment (libmsquic not installed)."
        );
        return false;
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

    private async Task<QuicConnection> ConnectAsync() => await ConnectToAsync(_options.Port);

    /// <summary>
    ///     This method opens a stream on a connection, sends a query and reads its answer,
    ///     returning the stream. It fails the test if the answer is wrong.
    /// </summary>
    private static async Task<QuicStream> OpenAnsweredStreamAsync(QuicConnection connection)
    {
        QuicStream stream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional);
        await SendAsync(stream, BuildQuery(0, "answered.example.com"));
        DnsMessage response = await ReceiveAsync(stream);
        Assert.Equal((ushort)0, response.Id);
        return stream;
    }

    /// <summary>
    ///     This method queries a port until it answers, for up to five seconds. A freed
    ///     connection slot is released on the server a moment after the client closes.
    /// </summary>
    private static async Task<DnsMessage> QueryEventuallyAsync(int port)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        DnsMessage response = null;

        while (response is null && DateTime.UtcNow < deadline)
        {
            response = await TryQueryAsync(IPAddress.Loopback, port);

            if (response is null)
                await Task.Delay(100);
        }

        return response;
    }

    private static async Task<QuicConnection> ConnectToAsync(
        int port,
        TimeSpan? keepAlive = null
    ) =>
        await QuicConnection.ConnectAsync(
            new QuicClientConnectionOptions
            {
                KeepAliveInterval = keepAlive ?? Timeout.InfiniteTimeSpan,
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, port),
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

    private static int GetFreeUdpPort()
    {
        using UdpClient probe = new(0);
        return ((IPEndPoint)probe.Client.LocalEndPoint).Port;
    }

    private static DnsMessage BuildQuery(ushort id, string name)
    {
        DnsMessage query = new() { Id = id, RecursionDesired = true };
        query.Questions.Add(
            new DnsQuestion(DnsName.Parse(name), DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet)
        );
        return query;
    }

    private static async Task SendAsync(QuicStream stream, DnsMessage message)
    {
        byte[] body = DnsMessageWriter.Write(message);
        byte[] framed = new byte[2 + body.Length];
        BinaryPrimitives.WriteUInt16BigEndian(framed, (ushort)body.Length);
        body.CopyTo(framed.AsSpan(2));

        await stream.WriteAsync(framed, completeWrites: true);
    }

    private static async Task<DnsMessage> ReceiveAsync(QuicStream stream)
    {
        byte[] lengthPrefix = new byte[2];
        await ReadExactAsync(stream, lengthPrefix);

        byte[] body = new byte[BinaryPrimitives.ReadUInt16BigEndian(lengthPrefix)];
        await ReadExactAsync(stream, body);

        return DnsMessageReader.Read(body);
    }

    private static async Task ReadExactAsync(QuicStream stream, byte[] buffer)
    {
        int totalRead = 0;

        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(totalRead));

            if (read == 0)
                throw new IOException(
                    "The stream ended before the expected number of bytes arrived."
                );

            totalRead += read;
        }
    }

    /// <summary>
    ///     This class is a fake query dispatcher in place of the resolvers, which other test
    ///     projects cover. It answers every query with a fixed A record, so these tests exercise
    ///     only the QUIC transport.
    /// </summary>
    private sealed class EchoingDispatcher : IDnsQueryDispatcher
    {
        public ValueTask<DnsMessage> DispatchAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        )
        {
            DnsQuestion question = context.Request.Questions[0];

            // A name under "drop." fails the dispatch, which makes the pipeline drop the query.
            if (question.Name.ToString().StartsWith("drop.", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Dispatch failed on purpose.");

            DnsMessage response = new()
            {
                Id = context.Request.Id,
                IsResponse = true,
                Opcode = context.Request.Opcode,
                IsAuthoritativeAnswer = true,
                RecursionDesired = context.Request.RecursionDesired,
                ResponseCode = DnsResponseCodeEnum.NoError,
            };
            response.Questions.Add(question);
            response.Answers.Add(
                new DnsResourceRecord(
                    question.Name,
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet,
                    60,
                    new ARecordDataCodec().Write(new ARecordData(IPAddress.Parse("203.0.113.1")))
                )
            );

            return ValueTask.FromResult(response);
        }
    }

    /// <summary>
    ///     This class is a fake <see cref="IZoneTransferService" /> that yields two fixed messages
    ///     for any AXFR or IXFR query, enough to check that the listener writes every message on
    ///     the same stream and completes it only after the last.
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
}
