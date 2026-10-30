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
///     certificate discovery, encrypted round trips for both supported ciphers, one transaction
///     per TCP connection, and silent drops for packets that cannot be authenticated.
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
    public async Task Udp_ASmallQueryWithALargeAnswerGetsAnEncryptedTruncatedReplyNoLargerThanTheQuery()
    {
        DnsCryptCertificateEntry certificateEntry = fixture.CertificatesByEsVersion[
            DnsCryptEsVersion.XChaCha20Poly1305
        ];
        DnsMessage query = DnsCryptWireTestHelper.BuildQuery(11, EchoResolver.LargeQuestionName);

        // 192 bytes of padded plaintext make a 260-byte packet.
        (byte[] packet, DnsCryptWireTestHelper.ClientQuerySession session) =
            DnsCryptWireTestHelper.BuildEncryptedQueryPacket(
                certificateEntry,
                query,
                minimumPaddedLength: 192
            );
        Assert.Equal(260, packet.Length);

        using UdpClient client = new();
        client.Connect(IPAddress.Loopback, DnsCryptListenerTestFixture.Port);
        await client.SendAsync(packet);
        byte[] responsePacket = await ReceiveWithTimeoutAsync(client);

        Assert.True(responsePacket.Length <= packet.Length);

        DnsMessage response = DnsCryptWireTestHelper.DecryptResponsePacket(
            certificateEntry,
            session,
            responsePacket,
            out _,
            out _
        );

        Assert.Equal(query.Id, response.Id);
        Assert.True(response.IsTruncated);
        Assert.Empty(response.Answers);
    }

    [Fact]
    public async Task Udp_ALargePaddedQueryGetsTheFullAnswer()
    {
        DnsCryptCertificateEntry certificateEntry = fixture.CertificatesByEsVersion[
            DnsCryptEsVersion.XSalsa20Poly1305
        ];
        DnsMessage query = DnsCryptWireTestHelper.BuildQuery(
            12,
            EchoResolver.MediumQuestionName,
            ednsPayloadSize: 4096
        );

        // The client pads the query so the 1092-byte packet leaves room for the answer.
        (byte[] packet, DnsCryptWireTestHelper.ClientQuerySession session) =
            DnsCryptWireTestHelper.BuildEncryptedQueryPacket(
                certificateEntry,
                query,
                minimumPaddedLength: 1024
            );

        using UdpClient client = new();
        client.Connect(IPAddress.Loopback, DnsCryptListenerTestFixture.Port);
        await client.SendAsync(packet);
        byte[] responsePacket = await ReceiveWithTimeoutAsync(client);

        Assert.True(responsePacket.Length <= packet.Length);

        DnsMessage response = DnsCryptWireTestHelper.DecryptResponsePacket(
            certificateEntry,
            session,
            responsePacket,
            out _,
            out _
        );

        Assert.False(response.IsTruncated);
        Assert.Equal(50, response.Answers.Count);
    }

    [Fact]
    public async Task Tcp_ALargeAnswerIsNotLimitedToTheQuerySize()
    {
        DnsCryptCertificateEntry certificateEntry = fixture.CertificatesByEsVersion[
            DnsCryptEsVersion.XChaCha20Poly1305
        ];
        (byte[] packet, DnsCryptWireTestHelper.ClientQuerySession session) =
            DnsCryptWireTestHelper.BuildEncryptedQueryPacket(
                certificateEntry,
                DnsCryptWireTestHelper.BuildQuery(13, EchoResolver.LargeQuestionName)
            );

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

        Assert.True(responsePacket.Length > packet.Length);
        Assert.False(response.IsTruncated);
        Assert.Equal(150, response.Answers.Count);
    }

    [Fact]
    public async Task Tcp_ClosesTheConnectionAfterTheAnswerAndDoesNotAnswerASecondQuery()
    {
        DnsCryptCertificateEntry certificateEntry = fixture.CertificatesByEsVersion[
            DnsCryptEsVersion.XChaCha20Poly1305
        ];

        using TcpClient client = new();
        await client.ConnectAsync(IPAddress.Loopback, DnsCryptListenerTestFixture.Port);
        using NetworkStream stream = client.GetStream();

        DnsMessage query = DnsCryptWireTestHelper.BuildEchoQuery(1);
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

        Assert.Equal(1, response.Id);

        // DNSCrypt v2 section 6: "After having received a response from the resolver, the client
        // and the resolver must close the TCP connection. Multiple transactions over the same TCP
        // connections are not allowed by this revision of the protocol."
        await AssertConnectionClosedAsync(stream);
    }

    [Fact]
    public async Task Tcp_ClosesTheConnectionAfterACertificateDiscoveryAnswer()
    {
        using TcpClient client = new();
        await client.ConnectAsync(IPAddress.Loopback, DnsCryptListenerTestFixture.Port);
        using NetworkStream stream = client.GetStream();

        DnsMessage discoveryQuery = new() { Id = 2, Opcode = DnsOpcodeEnum.Query };
        discoveryQuery.Questions.Add(
            new DnsQuestion(
                DnsName.Parse($"2.dnscrypt-cert.{DnsCryptListenerTestFixture.ProviderName}"),
                DnsResourceRecordTypeEnum.Txt,
                DnsClassEnum.Internet
            )
        );

        await WriteFramedAsync(stream, DnsMessageWriter.Write(discoveryQuery));
        List<byte[]> certificates = DnsCryptWireTestHelper.ExtractCertificatesFromDiscoveryResponse(
            await ReadFramedAsync(stream)
        );

        Assert.Equal(2, certificates.Count);
        await AssertConnectionClosedAsync(stream);
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

    /// <summary>
    ///     This method asserts that the server closes the connection without sending anything
    ///     more: the read ends at once with end of stream, or with a reset when the server closed
    ///     with unread queries still queued from the client.
    /// </summary>
    private static async Task AssertConnectionClosedAsync(NetworkStream stream)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        byte[] buffer = new byte[1];

        try
        {
            Assert.Equal(0, await stream.ReadAsync(buffer, timeout.Token));
        }
        catch (IOException)
        {
            // A reset also shows the server closed the connection.
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("The server kept the connection open after its answer.");
        }
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
