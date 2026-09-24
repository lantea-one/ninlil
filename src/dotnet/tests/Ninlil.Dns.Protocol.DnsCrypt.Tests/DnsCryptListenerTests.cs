using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.DnsCrypt;
using Ninlil.Dns.Server.Dnssec.Algorithms;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsCrypt.Tests;

/// <summary>
///     This suite exercises <c>Ninlil.Dns.Protocol.DnsCrypt</c>'s real UDP and TCP listeners
///     end-to-end, over real loopback sockets, against a real
///     <see cref="Ninlil.Dns.Server.Core.Pipeline.DnsQueryProcessingPipeline" /> --
///     certificate discovery, encrypted query/response round-trips for both supported ciphers,
///     and the listener's silent-drop behavior for a packet it cannot authenticate. This is this
///     project's permanent, hybrid-testing counterpart to the 12-check scratch smoke test 3f's
///     own delivery was originally verified with.
/// </summary>
public sealed class DnsCryptListenerTests(DnsCryptListenerTestFixture fixture)
    : IClassFixture<DnsCryptListenerTestFixture>
{
    [Fact]
    public async Task CertificateDiscovery_PublishesValidSignedCertificatesForBothCiphers()
    {
        using UdpClient client = new();
        client.Connect(IPAddress.Loopback, DnsCryptListenerTestFixture.Port);

        DnsMessage discoveryQuery = new() { Id = 1, Opcode = DnsOpcodeEnum.Query };
        discoveryQuery.Questions.Add(
            new DnsQuestion(
                DnsName.Parse($"2.dnscrypt-cert.{DnsCryptListenerTestFixture.ProviderName}"),
                DnsResourceRecordTypeEnum.Txt,
                DnsClassEnum.Internet
            )
        );

        await client.SendAsync(DnsMessageWriter.Write(discoveryQuery));
        byte[] rawResponse = await ReceiveWithTimeoutAsync(client);

        List<byte[]> certificates = DnsCryptWireTestHelper.ExtractCertificatesFromDiscoveryResponse(
            rawResponse
        );
        Assert.Equal(2, certificates.Count);

        DnssecAlgorithmRegistry.TryGetAlgorithm(
            DnssecAlgorithmEnum.Ed25519,
            out IDnssecSigningAlgorithm algorithm
        );
        HashSet<DnsCryptEsVersion> seenEsVersions = [];

        foreach (byte[] certificate in certificates)
        {
            (byte[] signingInput, byte[] signature, DnsCryptEsVersion esVersion) =
                DnsCryptWireTestHelper.DecodeCertificateForVerification(certificate);

            Assert.True(
                algorithm.Verify(
                    signingInput,
                    signature,
                    fixture.ProviderKeyPair.PublicKeyWireFormat
                ),
                $"The {esVersion} certificate's Ed25519 signature did not verify against the provider's real public key."
            );
            seenEsVersions.Add(esVersion);
        }

        Assert.Equal(
            new HashSet<DnsCryptEsVersion>
            {
                DnsCryptEsVersion.XChaCha20Poly1305,
                DnsCryptEsVersion.XSalsa20Poly1305,
            },
            seenEsVersions
        );
    }

    [Theory]
    [InlineData(DnsCryptEsVersion.XChaCha20Poly1305)]
    [InlineData(DnsCryptEsVersion.XSalsa20Poly1305)]
    public async Task EncryptedQueryRoundTrip_Udp_ReturnsTheEchoResolversAnswer(
        DnsCryptEsVersion esVersion
    )
    {
        DnsCryptCertificateEntry certificateEntry = fixture.CertificatesByEsVersion[esVersion];
        DnsMessage query = DnsCryptWireTestHelper.BuildEchoQuery(42);
        (byte[] packet, DnsCryptWireTestHelper.ClientQuerySession session) =
            DnsCryptWireTestHelper.BuildEncryptedQueryPacket(certificateEntry, query);

        using UdpClient client = new();
        client.Connect(IPAddress.Loopback, DnsCryptListenerTestFixture.Port);
        await client.SendAsync(packet);
        byte[] responsePacket = await ReceiveWithTimeoutAsync(client);

        DnsMessage response = DnsCryptWireTestHelper.DecryptResponsePacket(
            certificateEntry,
            session,
            responsePacket,
            out byte[] resolverMagic,
            out byte[] echoedClientNonceHalf
        );

        Assert.Equal(DnsCryptEnvelopeCodec.ResolverMagic, resolverMagic);
        Assert.Equal(session.ClientNonceHalf, echoedClientNonceHalf);
        Assert.Equal(query.Id, response.Id);
        Assert.True(response.IsResponse);
        Assert.Single(response.Answers);
        Assert.Equal(EchoResolver.AnswerAddress, response.Answers[0].Data);
    }

    [Theory]
    [InlineData(DnsCryptEsVersion.XChaCha20Poly1305)]
    [InlineData(DnsCryptEsVersion.XSalsa20Poly1305)]
    public async Task EncryptedQueryRoundTrip_Tcp_ReturnsTheEchoResolversAnswer(
        DnsCryptEsVersion esVersion
    )
    {
        DnsCryptCertificateEntry certificateEntry = fixture.CertificatesByEsVersion[esVersion];
        DnsMessage query = DnsCryptWireTestHelper.BuildEchoQuery(99);
        (byte[] packet, DnsCryptWireTestHelper.ClientQuerySession session) =
            DnsCryptWireTestHelper.BuildEncryptedQueryPacket(certificateEntry, query);

        using TcpClient client = new();
        await client.ConnectAsync(IPAddress.Loopback, DnsCryptListenerTestFixture.Port);
        using NetworkStream stream = client.GetStream();

        await WriteFramedAsync(stream, packet);
        byte[] responsePacket = await ReadFramedAsync(stream);

        DnsMessage response = DnsCryptWireTestHelper.DecryptResponsePacket(
            certificateEntry,
            session,
            responsePacket,
            out _,
            out _
        );

        Assert.Equal(query.Id, response.Id);
        Assert.Single(response.Answers);
        Assert.Equal(EchoResolver.AnswerAddress, response.Answers[0].Data);
    }

    [Fact]
    public async Task Tcp_AnswersTwoSequentialQueriesOnOneConnection()
    {
        DnsCryptCertificateEntry certificateEntry = fixture.CertificatesByEsVersion[
            DnsCryptEsVersion.XChaCha20Poly1305
        ];

        using TcpClient client = new();
        await client.ConnectAsync(IPAddress.Loopback, DnsCryptListenerTestFixture.Port);
        using NetworkStream stream = client.GetStream();

        for (ushort id = 1; id <= 2; id++)
        {
            DnsMessage query = DnsCryptWireTestHelper.BuildEchoQuery(id);
            (byte[] packet, DnsCryptWireTestHelper.ClientQuerySession session) =
                DnsCryptWireTestHelper.BuildEncryptedQueryPacket(certificateEntry, query);

            await WriteFramedAsync(stream, packet);
            byte[] responsePacket = await ReadFramedAsync(stream);

            DnsMessage response = DnsCryptWireTestHelper.DecryptResponsePacket(
                certificateEntry,
                session,
                responsePacket,
                out _,
                out _
            );

            Assert.Equal(id, response.Id);
            Assert.Single(response.Answers);
        }
    }

    [Fact]
    public async Task UnrecognizedClientMagic_ProducesNoResponse()
    {
        byte[] bogusPacket =
        [
            .. System.Security.Cryptography.RandomNumberGenerator.GetBytes(8), // clientMagic, matches no tracked certificate
            .. System.Security.Cryptography.RandomNumberGenerator.GetBytes(32), // clientPublicKey
            .. System.Security.Cryptography.RandomNumberGenerator.GetBytes(12), // clientNonceHalf
            .. System.Security.Cryptography.RandomNumberGenerator.GetBytes(64), // encryptedQuery
        ];

        await AssertNoResponseAsync(bogusPacket);
    }

    [Fact]
    public async Task TamperedCiphertext_ProducesNoResponse()
    {
        DnsCryptCertificateEntry certificateEntry = fixture.CertificatesByEsVersion[
            DnsCryptEsVersion.XChaCha20Poly1305
        ];
        DnsMessage query = DnsCryptWireTestHelper.BuildEchoQuery(7);
        (byte[] packet, _) = DnsCryptWireTestHelper.BuildEncryptedQueryPacket(
            certificateEntry,
            query
        );

        // Flip a byte well inside the encrypted query, past every envelope field, so the
        // client-magic still matches a real certificate but AEAD authentication fails.
        packet[^1] ^= 0xFF;

        await AssertNoResponseAsync(packet);
    }

    private static async Task<byte[]> ReceiveWithTimeoutAsync(UdpClient client)
    {
        // UdpClient.ReceiveAsync does not honor Client.ReceiveTimeout (that socket option only
        // applies to the synchronous Receive path) -- a real timeout needs an explicit
        // CancellationToken instead, so a genuinely broken listener fails this test quickly
        // rather than hanging the whole run.
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        return (await client.ReceiveAsync(timeout.Token)).Buffer;
    }

    private static async Task AssertNoResponseAsync(byte[] packet)
    {
        using UdpClient client = new();
        client.Connect(IPAddress.Loopback, DnsCryptListenerTestFixture.Port);
        await client.SendAsync(packet);

        // UdpClient.ReceiveAsync does not honor Client.ReceiveTimeout (that socket option only
        // applies to the synchronous Receive path) -- a real timeout needs an explicit
        // CancellationToken instead.
        using CancellationTokenSource timeout = new(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await client.ReceiveAsync(timeout.Token)
        );
    }

    private static async Task WriteFramedAsync(NetworkStream stream, byte[] payload)
    {
        byte[] lengthPrefix = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(lengthPrefix, (ushort)payload.Length);
        await stream.WriteAsync(lengthPrefix);
        await stream.WriteAsync(payload);
    }

    private static async Task<byte[]> ReadFramedAsync(NetworkStream stream)
    {
        byte[] lengthPrefix = new byte[2];
        await ReadExactAsync(stream, lengthPrefix);
        byte[] payload = new byte[BinaryPrimitives.ReadUInt16BigEndian(lengthPrefix)];
        await ReadExactAsync(stream, payload);
        return payload;
    }

    private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        int totalRead = 0;

        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(totalRead), timeout.Token);

            if (read == 0)
                throw new IOException("The connection closed before a complete frame was read.");

            totalRead += read;
        }
    }
}
