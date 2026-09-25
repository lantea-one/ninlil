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
///     <c>sdns://</c> stamp (<see cref="Definition.DnsCryptStampParser" />). Before it can
///     encrypt, it discovers the resolver's certificate: a plaintext TXT query, whose answers are
///     checked against the stamp's Ed25519 public key. That is the client side of what the
///     listener in <c>Ninlil.Dns.Protocol.DnsCrypt</c> answers. The crypto and wire-format code
///     from <c>Server.DnsCrypt</c> (<see cref="DnsCryptCryptoBox" />,
///     <see cref="DnsCryptCertificateCodec" />, <see cref="DnsCryptEnvelopeCodec" />) is shared
///     with the server role. The encrypted query goes over UDP, with TCP as a fallback (see
///     <see cref="SendUdpAsync" />).
///     <para>
///         A discovered certificate is not cached: there is no background service tracking the
///         upstream's certificate rotation, so every forwarded query discovers it again, at the
///         cost of an extra round trip.
///     </para>
/// </summary>
public sealed class DnsCryptForwarderClient : IUpstreamForwarderClient
{
    /// <summary>
    ///     This constant contains the largest UDP datagram this client reads.
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
    ///     to the upstream (the provider name includes that prefix), checks each returned
    ///     certificate's Ed25519 signature against the stamp's public key, and returns the valid
    ///     one with the highest serial.
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

            // One RFC 1035 §3.3 <character-string>: a length byte, then the certificate bytes,
            // as DnsCryptCertificateDiscoveryResponder writes it. It is read from the raw RDATA
            // because TxtRecordDataCodec goes through UTF-8 and would corrupt binary data.
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
    ///     This method sends one plaintext UDP query and decodes the first datagram received, for
    ///     certificate discovery. The source and message ID are not checked; the certificate
    ///     signature check protects the result.
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
    ///     This method sends an encrypted packet over UDP and returns the first datagram received,
    ///     still encrypted. It returns <see langword="null" />, so the caller retries over TCP,
    ///     only when the datagram fills the receive buffer; a decrypted answer with the TC flag set
    ///     is not retried.
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
    ///     This method sends an encrypted packet over a new, single-use TCP connection with a
    ///     2-byte length prefix (the same framing as RFC 1035 §4.2.2) and returns one
    ///     length-prefixed response, still encrypted.
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
