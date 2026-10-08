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
///     This class tests the DNSCrypt UDP and TCP listeners over loopback sockets, using the
///     listeners, pipeline and fake resolver of <see cref="DnsCryptListenerTestFixture" />:
///     certificate discovery, encrypted round trips for both supported ciphers, several queries
///     on one TCP connection, and silent drops for packets that cannot be authenticated.
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
    public async Task Tcp_AnswersEveryQueryOfAPipelinedBurstLargerThanTheConnectionLimit()
    {
        DnsCryptCertificateEntry certificateEntry = fixture.CertificatesByEsVersion[
            DnsCryptEsVersion.XChaCha20Poly1305
        ];

        using TcpClient client = new();
        await client.ConnectAsync(IPAddress.Loopback, DnsCryptListenerTestFixture.Port);
        using NetworkStream stream = client.GetStream();

        // Forty queries are written before any answer is read; a connection holds at most sixteen
        // in flight and reads on as answers are written.
        Dictionary<ushort, DnsCryptWireTestHelper.ClientQuerySession> sessions = [];

        for (ushort id = 1; id <= 40; id++)
        {
            (byte[] packet, DnsCryptWireTestHelper.ClientQuerySession session) =
                DnsCryptWireTestHelper.BuildEncryptedQueryPacket(
                    certificateEntry,
                    DnsCryptWireTestHelper.BuildEchoQuery(id)
                );
            sessions[id] = session;
            await WriteFramedAsync(stream, packet);
        }

        HashSet<ushort> answered = [];

        for (int count = 0; count < 40; count++)
        {
            byte[] responsePacket = await ReadFramedAsync(stream);

            // The answers may arrive in any order, so try each outstanding session.
            DnsMessage response = null;

            foreach ((ushort id, DnsCryptWireTestHelper.ClientQuerySession session) in sessions)
            {
                try
                {
                    response = DnsCryptWireTestHelper.DecryptResponsePacket(
                        certificateEntry,
                        session,
                        responsePacket,
                        out _,
                        out _
                    );
                    break;
                }
                catch (Exception)
                {
                    // Not this query's answer.
                }
            }

            Assert.NotNull(response);
            answered.Add(response.Id);
        }

        Assert.Equal(40, answered.Count);
    }

    [Fact]
    public async Task Tcp_PacketsThatAreNotAnsweredDoNotUseUpTheConnectionsQuerySlots()
    {
        DnsCryptCertificateEntry certificateEntry = fixture.CertificatesByEsVersion[
            DnsCryptEsVersion.XChaCha20Poly1305
        ];

        using TcpClient client = new();
        await client.ConnectAsync(IPAddress.Loopback, DnsCryptListenerTestFixture.Port);
        using NetworkStream stream = client.GetStream();

        // More junk packets than the connection's limit, none of which gets a reply.
        for (int count = 0; count < 40; count++)
            await WriteFramedAsync(
                stream,
                System.Security.Cryptography.RandomNumberGenerator.GetBytes(120)
            );

        DnsMessage query = DnsCryptWireTestHelper.BuildEchoQuery(5);
        (byte[] packet, DnsCryptWireTestHelper.ClientQuerySession session) =
            DnsCryptWireTestHelper.BuildEncryptedQueryPacket(certificateEntry, query);
        await WriteFramedAsync(stream, packet);

        DnsMessage response = DnsCryptWireTestHelper.DecryptResponsePacket(
            certificateEntry,
            session,
            await ReadFramedAsync(stream),
            out _,
            out _
        );

        Assert.Equal(5, response.Id);
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

        // Flip a byte inside the encrypted query, past the envelope fields, so the client-magic
        // still matches a certificate but AEAD authentication fails.
        packet[^1] ^= 0xFF;

        await AssertNoResponseAsync(packet);
    }

    private static async Task<byte[]> ReceiveWithTimeoutAsync(UdpClient client)
    {
        // UdpClient.ReceiveAsync ignores Client.ReceiveTimeout (it applies only to the
        // synchronous Receive), so a CancellationToken provides the timeout and a broken
        // listener fails this test quickly rather than hanging the run.
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        return (await client.ReceiveAsync(timeout.Token)).Buffer;
    }

    private static async Task AssertNoResponseAsync(byte[] packet)
    {
        using UdpClient client = new();
        client.Connect(IPAddress.Loopback, DnsCryptListenerTestFixture.Port);
        await client.SendAsync(packet);

        // UdpClient.ReceiveAsync ignores Client.ReceiveTimeout, so a CancellationToken provides
        // the timeout.
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
