using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Authoritative.Tests.Fakes;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Ninlil.Dns.Server.Tsig;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Transfer;

/// <summary>
///     This class checks that <see cref="TlsZoneTransferClient" /> completes a TLS handshake with a
///     loopback listener and carries a transfer over it. The framing and result shapes are covered
///     by <see cref="ZoneTransferPullCodecTests" /> through <see cref="TcpZoneTransferClient" />, so
///     they are not repeated here.
/// </summary>
public class TlsZoneTransferClientTests
{
    private static readonly DnsName Zone = DnsName.Parse("example.test.");

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

    /// <summary>
    ///     This method generates a throwaway, in-memory self-signed certificate for the fake
    ///     primary's TLS side -- <see cref="ZoneTransferClientOptions.ValidateServerCertificate" />
    ///     is set <see langword="false" /> on the client side below rather than trying to get this
    ///     test process to trust it, the same documented escape hatch a real homelab
    ///     operator would use for an untrusted primary.
    /// </summary>
    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using RSA rsa = RSA.Create(2048);
        CertificateRequest request = new(
            "CN=localhost",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddMinutes(5)
        );
    }

    [Fact]
    public async Task PullAsync_AgainstARealTlsPrimary_CompletesTheHandshakeAndPullsTheTransfer()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        using X509Certificate2 certificate = CreateSelfSignedCertificate();

        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        _ = Task.Run(async () =>
        {
            using TcpClient accepted = await listener.AcceptTcpClientAsync(cts.Token);
            await using SslStream sslStream = new(
                accepted.GetStream(),
                leaveInnerStreamOpen: false
            );
            await sslStream.AuthenticateAsServerAsync(
                new SslServerAuthenticationOptions { ServerCertificate = certificate },
                cts.Token
            );

            byte[] queryLengthPrefix = new byte[2];
            await sslStream.ReadExactlyAsync(queryLengthPrefix, cts.Token);
            byte[] queryBytes = new byte[(queryLengthPrefix[0] << 8) | queryLengthPrefix[1]];
            await sslStream.ReadExactlyAsync(queryBytes, cts.Token);
            DnsMessage query = DnsMessageReader.Read(queryBytes);

            DnsMessage response = new()
            {
                Id = query.Id,
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
            };

            byte[] responseBytes = DnsMessageWriter.Write(response);
            byte[] responseLengthPrefix =
            [
                (byte)(responseBytes.Length >> 8),
                (byte)responseBytes.Length,
            ];
            await sslStream.WriteAsync(responseLengthPrefix, cts.Token);
            await sslStream.WriteAsync(responseBytes, cts.Token);

            listener.Stop();
        });

        TlsZoneTransferClient client = new(
            new NoOpTsigService(),
            new StaticOptionsMonitor<ZoneTransferClientOptions>(
                new ZoneTransferClientOptions
                {
                    PrimaryTlsPort = port,
                    ConnectTimeout = TimeSpan.FromSeconds(5),
                    ReadTimeout = TimeSpan.FromSeconds(5),
                    ValidateServerCertificate = false,
                }
            )
        );

        ZoneTransferPullResult result = await client.PullAsync(
            IPAddress.Loopback,
            Zone,
            10,
            null,
            cts.Token
        );

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.True(result.IsAlreadyCurrent);
        Assert.Equal((uint)10, result.FinalSerial);
    }

    [Fact]
    public async Task PullAsync_AgainstAPlainTcpListenerNotSpeakingTls_FailsTheHandshakeCleanly()
    {
        // A real listener that never upgrades to TLS at all -- proves a handshake failure is
        // reported as a real Failure result, not an unhandled exception escaping PullAsync.
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        _ = Task.Run(async () =>
        {
            using TcpClient accepted = await listener.AcceptTcpClientAsync(cts.Token);
            // Read whatever the client sends (the TLS ClientHello) and never respond -- the
            // client's handshake will time out or fail to parse a response. A partial read is
            // fine and expected here; this is just draining the socket, not framing a
            // real message the way the codec's ReadExactlyAsync must.
#pragma warning disable CA2022
            await accepted.GetStream().ReadAsync(new byte[1024], cts.Token);
#pragma warning restore CA2022
        });

        TlsZoneTransferClient client = new(
            new NoOpTsigService(),
            new StaticOptionsMonitor<ZoneTransferClientOptions>(
                new ZoneTransferClientOptions
                {
                    PrimaryTlsPort = port,
                    ConnectTimeout = TimeSpan.FromMilliseconds(500),
                    ReadTimeout = TimeSpan.FromMilliseconds(500),
                    ValidateServerCertificate = false,
                }
            )
        );

        ZoneTransferPullResult result = await client.PullAsync(
            IPAddress.Loopback,
            Zone,
            null,
            null,
            cts.Token
        );

        Assert.False(result.Succeeded);
        Assert.Contains("handshake", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }
}
