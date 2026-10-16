using System.Buffers.Binary;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Authoritative.Tests.Fakes;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Ninlil.Dns.Server.Tsig;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Transfer;

/// <summary>
///     This class tests <see cref="QuicZoneTransferClient" />. Its pull logic is the shared
///     <c>ZoneTransferPullOverStream</c> covered by <see cref="ZoneTransferPullCodecTests" /> and
///     <see cref="TlsZoneTransferClientTests" />, and a QUIC handshake needs <c>libmsquic</c>, which
///     the test environment may lack (as for <c>DnsOverQuicListenerTests</c>). So it checks only
///     that an unsupported platform (<see cref="QuicConnection.IsSupported" /> false) gives a clean
///     failure result rather than an exception.
/// </summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
[SupportedOSPlatform("windows")]
public class QuicZoneTransferClientTests(TsigTransferFixture fixture)
    : IClassFixture<TsigTransferFixture>,
        IDisposable
{
    private static readonly DnsName Zone = DnsName.Parse("example.test.");

    private static readonly SslApplicationProtocol DoqApplicationProtocol = new("doq");

    // The scope the client's ITsigService comes from; it lives as long as the test.
    private readonly IServiceScope _scope = fixture.Host.Services.CreateScope();

    private readonly TsigTransferHost _host = fixture.Host;

    /// <inheritdoc />
    public void Dispose() => _scope.Dispose();

    /// <summary>
    ///     This class is a fake DoQ primary. It accepts one connection and one stream, records the
    ///     decoded query, and writes the framed messages a test-supplied handler returns for it.
    /// </summary>
    private sealed class FakeQuicPrimary : IAsyncDisposable
    {
        private readonly QuicListener _listener;
        private readonly X509Certificate2 _certificate;
        private readonly Task _serving;

        private FakeQuicPrimary(
            QuicListener listener,
            X509Certificate2 certificate,
            Func<DnsMessage, byte[], int, Task<List<DnsMessage>>> handler
        )
        {
            _listener = listener;
            _certificate = certificate;
            _serving = ServeOnceAsync(handler);
        }

        /// <summary>
        ///     This property contains the port the fake listens on.
        /// </summary>
        public int Port => _listener.LocalEndPoint.Port;

        /// <summary>
        ///     This property contains the query the fake received, or <see langword="null" /> when
        ///     none has arrived.
        /// </summary>
        public DnsMessage ReceivedQuery { get; private set; }

        /// <summary>
        ///     This method starts a fake primary on a free loopback port.
        /// </summary>
        public static async Task<FakeQuicPrimary> StartAsync(
            Func<DnsMessage, byte[], int, Task<List<DnsMessage>>> handler
        )
        {
            // The certificate is exported and re-imported so the private key is a persisted
            // one; MsQuic cannot always use an ephemeral in-memory key.
            using RSA rsa = RSA.Create(2048);
            CertificateRequest request = new(
                "CN=localhost",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1
            );
            using X509Certificate2 ephemeral = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-5),
                DateTimeOffset.UtcNow.AddMinutes(30)
            );
            X509Certificate2 certificate = X509CertificateLoader.LoadPkcs12(
                ephemeral.Export(X509ContentType.Pfx),
                null
            );

            QuicListener listener = await QuicListener.ListenAsync(
                new QuicListenerOptions
                {
                    ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
                    ApplicationProtocols = [DoqApplicationProtocol],
                    ConnectionOptionsCallback = (_, _, _) =>
                        ValueTask.FromResult(
                            new QuicServerConnectionOptions
                            {
                                DefaultCloseErrorCode = 0,
                                DefaultStreamErrorCode = 0,
                                ServerAuthenticationOptions = new SslServerAuthenticationOptions
                                {
                                    ApplicationProtocols = [DoqApplicationProtocol],
                                    ServerCertificate = certificate,
                                },
                            }
                        ),
                }
            );

            return new FakeQuicPrimary(listener, certificate, handler);
        }

        private async Task ServeOnceAsync(
            Func<DnsMessage, byte[], int, Task<List<DnsMessage>>> handler
        )
        {
            try
            {
                using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
                await using QuicConnection connection = await _listener.AcceptConnectionAsync(
                    timeout.Token
                );
                await using QuicStream stream = await connection.AcceptInboundStreamAsync(
                    timeout.Token
                );

                byte[] prefix = new byte[2];
                await stream.ReadExactlyAsync(prefix, timeout.Token);
                byte[] raw = new byte[BinaryPrimitives.ReadUInt16BigEndian(prefix)];
                await stream.ReadExactlyAsync(raw, timeout.Token);
                DnsMessage query = DnsMessageReader.Read(raw, out int tsigOffset);
                ReceivedQuery = query;

                List<DnsMessage> messages = await handler(query, raw, tsigOffset);

                for (int i = 0; i < messages.Count; i++)
                {
                    byte[] body = DnsMessageWriter.Write(messages[i]);
                    byte[] framed = new byte[2 + body.Length];
                    BinaryPrimitives.WriteUInt16BigEndian(framed, (ushort)body.Length);
                    body.CopyTo(framed.AsSpan(2));
                    await stream.WriteAsync(
                        framed,
                        completeWrites: i == messages.Count - 1,
                        timeout.Token
                    );
                }

                // Disposing the connection at once could discard a response the client has not
                // read yet; wait until the peer has acknowledged the end of the stream.
                await stream.WritesClosed.WaitAsync(timeout.Token);
            }
            catch (Exception exception)
                when (exception is QuicException or OperationCanceledException or IOException)
            {
                // The client gave up; the test's assertions report it.
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _listener.DisposeAsync();
            await _serving;
            _certificate.Dispose();
        }
    }

    private QuicZoneTransferClient CreateClient(int port) =>
        new(
            _scope.ServiceProvider.GetRequiredService<ITsigService>(),
            new StaticOptionsMonitor<ZoneTransferClientOptions>(
                new ZoneTransferClientOptions
                {
                    PrimaryQuicPort = port,
                    ConnectTimeout = TimeSpan.FromSeconds(5),
                    ReadTimeout = TimeSpan.FromSeconds(5),
                    // The fake primary's certificate is self-signed.
                    ValidateServerCertificate = false,
                }
            )
        );

    private static bool IsQuicAvailable() => QuicListener.IsSupported && QuicConnection.IsSupported;

    private sealed class NoOpTsigService : ITsigService
    {
        public Task<DnsMessage> SignAsync(
            DnsMessage message,
            string keyName,
            ushort originalId,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException("Not exercised -- this test uses no TSIG key.");

        public Task<DnsMessage> SignErrorResponseAsync(
            DnsMessage response,
            DnsTsigOptions requestTsig,
            TsigVerificationResult failure,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException("Not exercised -- this test uses no TSIG key.");

        public Task<TsigVerificationResult> VerifyAsync(
            byte[] rawMessage,
            int tsigRecordStartOffset,
            DnsMessage message,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException("Not exercised -- this test uses no TSIG key.");

        public Task<DnsMessage> SignResponseAsync(
            DnsMessage message,
            string keyName,
            ushort originalId,
            byte[] requestMac,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException("Not exercised -- this test uses no TSIG key.");

        public Task<DnsMessage> SignContinuationAsync(
            DnsMessage message,
            string keyName,
            ushort originalId,
            byte[] priorMac,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException("Not exercised -- this test uses no TSIG key.");

        public Task<TsigVerificationResult> VerifyResponseAsync(
            byte[] rawMessage,
            int tsigRecordStartOffset,
            DnsMessage message,
            byte[] requestMac,
            string expectedKeyName,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException("Not exercised -- this test uses no TSIG key.");

        public Task<TsigVerificationResult> VerifyContinuationAsync(
            byte[] rawMessage,
            int tsigRecordStartOffset,
            DnsMessage message,
            byte[] priorMac,
            IReadOnlyList<byte[]> unsignedMessageBytesSincePriorTsig,
            string expectedKeyName,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException("Not exercised -- this test uses no TSIG key.");
    }

    [Fact]
    public async Task PullAsync_WhenQuicIsNotSupportedOnThisPlatform_ReturnsACleanFailure()
    {
        if (QuicConnection.IsSupported)
            return; // Nothing to prove here in an environment that actually has libmsquic.

        QuicZoneTransferClient client = new(
            new NoOpTsigService(),
            new StaticOptionsMonitor<ZoneTransferClientOptions>(new ZoneTransferClientOptions())
        );

        ZoneTransferPullResult result = await client.PullAsync(
            IPAddress.Loopback,
            Zone,
            null,
            null
        );

        Assert.False(result.Succeeded);
        Assert.Contains("not supported", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PullAsync_WithATsigKey_SendsMessageIdZeroSignedWithOriginalIdZeroAndVerifies()
    {
        if (!IsQuicAvailable())
            return;

        string keyName = await _host.AddKeyAsync();

        // The fake primary is the real transfer service: it verifies the query's TSIG and signs
        // every response, so a pass proves the MAC covers the ID 0 the client sent.
        await using FakeQuicPrimary primary = await FakeQuicPrimary.StartAsync(
            (query, raw, tsigOffset) => _host.TransferAsync(query, raw, tsigOffset)
        );
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(20));

        ZoneTransferPullResult result = await CreateClient(primary.Port)
            .PullAsync(
                IPAddress.Loopback,
                DnsName.Parse(TsigTransferHost.ZoneName),
                null,
                keyName,
                cts.Token
            );

        Assert.NotNull(primary.ReceivedQuery);
        Assert.Equal(0, primary.ReceivedQuery.Id);
        Assert.Equal(0, primary.ReceivedQuery.Tsig.OriginalId);
        Assert.True(result.Succeeded, result.FailureReason);
        Assert.Equal((uint)100, result.FinalSerial);
    }

    [Fact]
    public async Task PullAsync_ForAResponseWithANonZeroMessageId_FailsTheTransfer()
    {
        if (!IsQuicAvailable())
            return;

        // An otherwise valid "already current" answer, but under ID 5.
        await using FakeQuicPrimary primary = await FakeQuicPrimary.StartAsync(
            (query, _, _) =>
                Task.FromResult<List<DnsMessage>>([
                    new DnsMessage
                    {
                        Id = 5,
                        IsResponse = true,
                        Opcode = DnsOpcodeEnum.Query,
                        ResponseCode = DnsResponseCodeEnum.NoError,
                        Questions = { query.Questions[0] },
                        Answers =
                        {
                            new DnsResourceRecord(
                                Zone,
                                DnsResourceRecordTypeEnum.Soa,
                                DnsClassEnum.Internet,
                                3600,
                                DnsRecordDataCodecRegistry.Encode(
                                    DnsResourceRecordTypeEnum.Soa,
                                    new SoaRecordData(DnsName.Root, DnsName.Root, 10, 0, 0, 0, 0)
                                )
                            ),
                        },
                    },
                ])
        );
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(20));

        ZoneTransferPullResult result = await CreateClient(primary.Port)
            .PullAsync(IPAddress.Loopback, Zone, 10, null, cts.Token);

        Assert.False(result.Succeeded);
        Assert.Contains("ID", result.FailureReason);
    }
}
