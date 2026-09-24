using System.Buffers.Binary;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
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
///     This class exercises a real, running <see cref="DnsOverQuicListener" /> over real loopback
///     QUIC connections -- the permanent xUnit counterpart to this project's own 3f scratch smoke
///     test, per this repo's hybrid testing convention (smoke tests always, plus a real xUnit
///     suite for stable, unlikely-to-churn pieces).
///     <para>
///         QUIC needs <c>libmsquic</c> (and, on Linux, OpenSSL) installed as a native OS
///         dependency this repo does not control -- every test here that needs a real connection
///         checks <see cref="QuicListener.IsSupported" />/<see cref="QuicConnection.IsSupported" />
///         and exits early (reported as passed, not skipped -- this repo has no
///         skip-conditionally-at-runtime convention yet, and adding a new package dependency just
///         for that felt disproportionate) rather than failing an environment that simply does not
///         have it installed, matching <see cref="DnsOverQuicListener" />'s own "log and don't
///         start" behavior for the exact same condition.
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

        // The listener's own QuicListener.ListenAsync call happens on a background task; give it
        // a moment to actually bind before any test tries to connect against it.
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
            Id = 777,
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

        // FakeZoneTransferService (registered in InitializeAsync's own scope factory) yields two
        // messages for any AXFR query -- both must arrive, on this same stream, before it signals
        // completion.
        DnsMessage first = await ReceiveAsync(stream);
        DnsMessage second = await ReceiveAsync(stream);

        Assert.Equal((ushort)1, first.Id);
        Assert.Equal((ushort)2, second.Id);
    }

    [Fact]
    public void QuicSupportIsReported()
    {
        // Documents, rather than asserts, environment capability -- every test below becomes a
        // no-op (not a failure) when this is false.
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

        DnsMessage query = BuildQuery(1234, "example.com");
        await SendAsync(stream, query);
        DnsMessage response = await ReceiveAsync(stream);

        Assert.Equal(query.Id, response.Id);
        Assert.True(response.IsResponse);
        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        Assert.Single(response.Answers);
        Assert.Equal(DnsResourceRecordTypeEnum.A, response.Answers[0].Type);
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
            await SendAsync(stream, BuildQuery(1, "first.example.com"));
            await ReceiveAsync(stream);
        }

        await using QuicConnection second = await ConnectAsync();
        await using QuicStream secondStream = await second.OpenOutboundStreamAsync(
            QuicStreamType.Bidirectional
        );
        await SendAsync(secondStream, BuildQuery(2, "second.example.com"));
        DnsMessage response = await ReceiveAsync(secondStream);

        Assert.Equal((ushort)2, response.Id);
    }

    [Fact]
    public async Task MultipleConcurrentStreamsOnOneConnection_AreAllAnswered()
    {
        if (!IsQuicAvailable())
            return;

        await using QuicConnection connection = await ConnectAsync();

        async Task<DnsMessage> QueryAsync(ushort id)
        {
            await using QuicStream stream = await connection.OpenOutboundStreamAsync(
                QuicStreamType.Bidirectional
            );
            await SendAsync(stream, BuildQuery(id, $"q{id}.example.com"));
            return await ReceiveAsync(stream);
        }

        DnsMessage[] responses = await Task.WhenAll(QueryAsync(10), QueryAsync(11), QueryAsync(12));

        Assert.Equal(
            [10, 11, 12],
            responses.Select(response => (int)response.Id).OrderBy(id => id)
        );
    }

    /// <summary>
    ///     This method reports whether this environment can actually run a real QUIC connection,
    ///     writing a note to the test output when it cannot, since every calling test's early
    ///     return would otherwise look like an untested no-op with no explanation.
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
    ///     This method builds a real <see cref="IServiceScopeFactory" /> resolving
    ///     <see cref="IZoneTransferService" /> to a <see cref="FakeZoneTransferService" /> --
    ///     <see cref="DnsOverQuicListener" />'s own AXFR/IXFR branch needs a real scope factory,
    ///     but this test project has no reason to stand up the real
    ///     <c>Ninlil.Dns.Server.Authoritative</c> DI graph (a real zone cache, TSIG, and storage)
    ///     just to prove the transport/framing side of zone transfer works.
    /// </summary>
    private static IServiceScopeFactory BuildScopeFactory() =>
        new ServiceCollection()
            .AddScoped<IZoneTransferService, FakeZoneTransferService>()
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

    private async Task<QuicConnection> ConnectAsync() =>
        await QuicConnection.ConnectAsync(
            new QuicClientConnectionOptions
            {
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, _options.Port),
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
    ///     This class stands in for the real authoritative/recursive engines, which this test
    ///     deliberately does not exercise -- their own correctness is this project's other test
    ///     projects' job. This dispatcher only ever answers with a fixed A record, letting these
    ///     tests focus entirely on the QUIC transport itself.
    /// </summary>
    private sealed class EchoingDispatcher : IDnsQueryDispatcher
    {
        public ValueTask<DnsMessage> DispatchAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        )
        {
            DnsQuestion question = context.Request.Questions[0];
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
    ///     This class stands in for a real <see cref="IZoneTransferService" />, yielding two
    ///     fixed messages for any AXFR/IXFR query -- enough to prove
    ///     <see cref="DnsOverQuicListener" />'s own multi-message zone-transfer branch actually
    ///     writes every message onto the same stream and only completes it after the last one,
    ///     without needing a real zone cache or TSIG service.
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
