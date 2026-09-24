using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Forwarding;
using Ninlil.Dns.Server.DnsCrypt;
using Ninlil.Dns.Server.Dnssec.Algorithms;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Clients;

/// <summary>
///     This class forwards a query to an upstream DNSCrypt resolver, addressed via an
///     <c>sdns://</c> stamp (<see cref="Definition.DnsCryptStampParser" />). Unlike every other
///     <see cref="IUpstreamForwarderClient" />, this one must complete a full certificate
///     discovery round trip -- a plaintext TXT query, Ed25519 signature verification against the
///     stamp's own embedded public key -- before it can encrypt anything, the exact client-side
///     mirror of what <c>Ninlil.Dns.Protocol.DnsCrypt</c>'s own listener answers for a
///     connecting client. This project's existing <c>Server.DnsCrypt</c> crypto/certificate
///     wire-format code (<see cref="DnsCryptCryptoBox" />, <see cref="DnsCryptCertificateCodec" />,
///     <see cref="DnsCryptEnvelopeCodec" />) is reused directly rather than reimplemented, since
///     the construction is symmetric between the two roles.
///     <para>
///         A discovered certificate is deliberately never cached across calls: this client has
///         no background rotation-tracking service of its own (unlike this resolver's own
///         server-role <c>DnsCryptCertificateRotationService</c>), so re-discovering on every
///         forwarded query is the only way to notice the upstream rotating its own certificate
///         without this client silently going stale.
///     </para>
/// </summary>
public sealed class DnsCryptForwarderClient : IUpstreamForwarderClient
{
    /// <summary>
    ///     This constant contains the largest UDP datagram this client will ever read.
    /// </summary>
    private const int MaximumDatagramSize = 65527;

    /// <inheritdoc />
    public UpstreamForwarderProtocolEnum Protocol => UpstreamForwarderProtocolEnum.DnsCrypt;

    /// <inheritdoc />
    public async Task<DnsMessage> ForwardAsync(
        UpstreamForwarderDefinition definition,
        DnsMessage query,
        CancellationToken stoppingToken = default
    )
    {
        IPEndPoint endpoint = new(definition.Address, definition.Port);

        DnsCryptCertificate certificate = await DiscoverCertificateAsync(
            definition,
            endpoint,
            stoppingToken
        );

        (byte[] clientPublicKey, byte[] clientSecretKey) = DnsCryptCryptoBox.GenerateKeyPair(
            certificate.EsVersion
        );
        byte[] clientNonceHalf = RandomNumberGenerator.GetBytes(
            DnsCryptEnvelopeCodec.HalfNonceLength
        );

        byte[] queryPacket = DnsCryptEnvelopeCodec.EncryptQuery(
            certificate.EsVersion,
            certificate.ClientMagic,
            clientPublicKey,
            clientSecretKey,
            certificate.ResolverPublicKey,
            clientNonceHalf,
            DnsMessageWriter.Write(query)
        );

        byte[] responsePacket = await SendUdpAsync(queryPacket, endpoint, stoppingToken);

        if (responsePacket is null)
            responsePacket = await SendTcpAsync(queryPacket, endpoint, stoppingToken);

        byte[] plaintext = DnsCryptEnvelopeCodec.DecryptResponse(
            certificate.EsVersion,
            responsePacket,
            clientNonceHalf,
            clientSecretKey,
            certificate.ResolverPublicKey
        );

        return DnsMessageReader.Read(plaintext);
    }

    /// <summary>
    ///     This method sends the plaintext <c>2.dnscrypt-cert.&lt;providerName&gt;</c> TXT query
    ///     to the upstream, verifies every returned certificate's own Ed25519 signature against
    ///     the stamp's own embedded public key, and returns the highest-serial one still
    ///     currently valid.
    /// </summary>
    /// <exception cref="CryptographicException">
    ///     Thrown when no returned certificate both verifies and is currently valid.
    /// </exception>
    private static async Task<DnsCryptCertificate> DiscoverCertificateAsync(
        UpstreamForwarderDefinition definition,
        IPEndPoint endpoint,
        CancellationToken stoppingToken
    )
    {
        if (
            !DnssecAlgorithmRegistry.TryGetAlgorithm(
                DnssecAlgorithmEnum.Ed25519,
                out IDnssecSigningAlgorithm algorithm
            )
        )
            throw new InvalidOperationException(
                "The Ed25519 signing algorithm, required to verify a discovered DNSCrypt certificate, is not registered."
            );

        DnsMessage discoveryQuery = new()
        {
            Id = (ushort)Random.Shared.Next(ushort.MaxValue),
            Opcode = DnsOpcodeEnum.Query,
            RecursionDesired = false,
        };
        discoveryQuery.Questions.Add(
            new DnsQuestion(
                DnsName.Parse(definition.DnsCryptProviderName),
                DnsResourceRecordTypeEnum.Txt,
                DnsClassEnum.Internet
            )
        );

        DnsMessage discoveryResponse = await SendPlainUdpAsync(
            DnsMessageWriter.Write(discoveryQuery),
            endpoint,
            stoppingToken
        );

        DnsCryptCertificate best = null;
        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (DnsResourceRecord answer in discoveryResponse.Answers)
        {
            if (answer.Type != DnsResourceRecordTypeEnum.Txt || answer.Data.Length < 1)
                continue;

            // A single RFC 1035 §3.3 <character-string>: a one-byte length prefix followed by
            // the certificate's own raw bytes, built the same way
            // DnsCryptCertificateDiscoveryResponder writes it -- read directly off the raw
            // RDATA rather than through TxtRecordDataCodec, which round-trips through UTF-8 and
            // would corrupt this certificate's arbitrary binary content.
            int certificateLength = answer.Data[0];

            if (answer.Data.Length < 1 + certificateLength)
                continue;

            DnsCryptCertificate candidate;

            try
            {
                candidate = DnsCryptCertificateCodec.Decode(
                    answer.Data.AsSpan(1, certificateLength)
                );
            }
            catch (FormatException)
            {
                continue;
            }

            if (candidate.ValidFrom > now || candidate.ValidUntil <= now)
                continue;

            byte[] signingInput = DnsCryptCertificateCodec.BuildSignedData(candidate);

            if (
                !algorithm.Verify(
                    signingInput,
                    candidate.Signature,
                    definition.DnsCryptProviderPublicKey
                )
            )
                continue;

            if (best is null || candidate.Serial > best.Serial)
                best = candidate;
        }

        return best
            ?? throw new CryptographicException(
                $"No currently valid, correctly signed DNSCrypt certificate was discovered for provider '{definition.DnsCryptProviderName}'."
            );
    }

    /// <summary>
    ///     This method sends a single plaintext UDP query and reads back a single response
    ///     datagram, decoded -- used only for certificate discovery, which is always sent in the
    ///     clear.
    /// </summary>
    private static async Task<DnsMessage> SendPlainUdpAsync(
        byte[] queryBytes,
        IPEndPoint endpoint,
        CancellationToken stoppingToken
    )
    {
        using Socket socket = new(endpoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        await socket.SendToAsync(queryBytes, endpoint, stoppingToken);

        byte[] receiveBuffer = new byte[MaximumDatagramSize];
        SocketReceiveFromResult result = await socket.ReceiveFromAsync(
            receiveBuffer,
            endpoint,
            stoppingToken
        );

        return DnsMessageReader.Read(receiveBuffer.AsSpan(0, result.ReceivedBytes));
    }

    /// <summary>
    ///     This method sends a single already-encrypted packet over UDP and reads back a single
    ///     response datagram, raw -- returning <see langword="null" /> if the datagram looks
    ///     truncated (exactly filled this client's own receive buffer), signaling the caller to
    ///     retry over TCP instead, the same convention <c>PlainForwarderClient</c> uses for its
    ///     own plaintext queries.
    /// </summary>
    private static async Task<byte[]> SendUdpAsync(
        byte[] packet,
        IPEndPoint endpoint,
        CancellationToken stoppingToken
    )
    {
        using Socket socket = new(endpoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        await socket.SendToAsync(packet, endpoint, stoppingToken);

        byte[] receiveBuffer = new byte[MaximumDatagramSize];
        SocketReceiveFromResult result = await socket.ReceiveFromAsync(
            receiveBuffer,
            endpoint,
            stoppingToken
        );

        if (result.ReceivedBytes >= MaximumDatagramSize)
            return null;

        return receiveBuffer.AsSpan(0, result.ReceivedBytes).ToArray();
    }

    /// <summary>
    ///     This method sends a single already-encrypted packet over a fresh, single-use TCP
    ///     connection, 2-byte length-prefixed per the DNSCrypt spec's own TCP framing (identical
    ///     to RFC 1035 §4.2.2's plain-DNS framing), and reads back exactly one length-prefixed
    ///     response, raw.
    /// </summary>
    private static async Task<byte[]> SendTcpAsync(
        byte[] packet,
        IPEndPoint endpoint,
        CancellationToken stoppingToken
    )
    {
        using Socket socket = new(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(endpoint, stoppingToken);

        byte[] lengthPrefix = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(lengthPrefix, (ushort)packet.Length);
        await socket.SendAsync(lengthPrefix, stoppingToken);
        await socket.SendAsync(packet, stoppingToken);

        await ReadExactAsync(socket, lengthPrefix, stoppingToken);
        ushort responseLength = BinaryPrimitives.ReadUInt16BigEndian(lengthPrefix);
        byte[] responseBuffer = new byte[responseLength];
        await ReadExactAsync(socket, responseBuffer, stoppingToken);

        return responseBuffer;
    }

    /// <summary>
    ///     This method fills a buffer completely from a socket, throwing if the connection closes
    ///     before every requested byte arrives.
    /// </summary>
    private static async Task ReadExactAsync(
        Socket socket,
        byte[] buffer,
        CancellationToken stoppingToken
    )
    {
        int totalRead = 0;

        while (totalRead < buffer.Length)
        {
            int read = await socket.ReceiveAsync(buffer.AsMemory(totalRead), stoppingToken);

            if (read == 0)
                throw new IOException(
                    "The forwarder closed the connection before a complete response arrived."
                );

            totalRead += read;
        }
    }
}
