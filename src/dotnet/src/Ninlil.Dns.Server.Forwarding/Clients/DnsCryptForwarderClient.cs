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
///     with the server role. The encrypted query goes over UDP; a reply that has
///     the TC flag set (or fills the receive buffer) is asked again over TCP.
///     <para>
///         A discovered certificate is not cached: there is no background service tracking the
///         upstream's certificate rotation, so every forwarded query discovers it again, at the
///         cost of an extra round trip.
///     </para>
///     <para>
///         Each forwarded query, and the TCP retry of a truncated one, is encrypted anew with its
///         own key pair, nonce and random message ID. The decrypted reply must carry that ID and
///         repeat the question. Over UDP any datagram that comes from another source, fails to
///         decrypt or authenticate, or does not match is ignored and the wait goes on until the
///         caller's token ends it; over TCP a bad reply fails the attempt with an
///         <see cref="InvalidDataException" />. The reply handed back carries the ID of the query
///         the caller passed in. Certificate discovery keeps its own plaintext query, protected by
///         the certificate signature instead.
///     </para>
/// </summary>
public sealed class DnsCryptForwarderClient : IUpstreamForwarderClient
{
    private readonly Func<ushort> newMessageId;

    /// <summary>
    ///     This constructor builds a client that draws its message IDs from the cryptographic
    ///     random source.
    /// </summary>
    public DnsCryptForwarderClient()
        : this(null) { }

    /// <summary>
    ///     This constructor lets tests fix the message ID of the forwarded query.
    /// </summary>
    /// <param name="newMessageId">
    ///     Returns the ID of each forwarded query; <see langword="null" /> uses
    ///     <see cref="ForwarderQueryMessage.NewRandomId" />.
    /// </param>
    internal DnsCryptForwarderClient(Func<ushort> newMessageId)
    {
        this.newMessageId = newMessageId ?? ForwarderQueryMessage.NewRandomId;
    }

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

        DnsMessage response = await ExchangeAsync(
            certificate,
            query,
            endpoint,
            overTcp: false,
            stoppingToken
        );

        if (response is null || response.IsTruncated)
        {
            // The UDP answer was cut short (or filled the buffer): ask again over TCP, which has
            // no size limit. The retry is a new query with its own key pair, nonce and ID.
            response = await ExchangeAsync(
                certificate,
                query,
                endpoint,
                overTcp: true,
                stoppingToken
            );
        }

        response.Id = query.Id;

        return response;
    }

    /// <summary>
    ///     This method encrypts the query with a fresh client key pair, a fresh nonce half and a
    ///     fresh message ID, sends it over UDP or TCP and returns the decrypted reply. Every call
    ///     is a new query, so the UDP and TCP attempts never share a nonce under one key pair (the
    ///     DNSCrypt v2 specification, section 2, requires a client nonce to be unique per
    ///     client secret key and resolver public key).
    /// </summary>
    /// <param name="certificate">The certificate the resolver's keys and magic come from.</param>
    /// <param name="query">The caller's query; its ID is replaced on the wire.</param>
    /// <param name="endpoint">The upstream's address and port.</param>
    /// <param name="overTcp">Whether to use TCP instead of UDP.</param>
    /// <param name="stoppingToken">Ends the attempt, including a wait for a valid UDP reply.</param>
    /// <returns>
    ///     The reply, or <see langword="null" /> when a UDP datagram filled the receive buffer and
    ///     the caller must use TCP.
    /// </returns>
    /// <exception cref="InvalidDataException">
    ///     Thrown when a TCP reply does not decrypt or does not answer the query: a stream has no
    ///     stray datagrams to skip, so the attempt fails.
    /// </exception>
    private async Task<DnsMessage> ExchangeAsync(
        DnsCryptCertificate certificate,
        DnsMessage query,
        IPEndPoint endpoint,
        bool overTcp,
        CancellationToken stoppingToken
    )
    {
        (byte[] clientPublicKey, byte[] clientSecretKey) = DnsCryptCryptoBox.GenerateKeyPair(
            certificate.EsVersion
        );
        byte[] clientNonceHalf = RandomNumberGenerator.GetBytes(
            DnsCryptEnvelopeCodec.HalfNonceLength
        );
        DnsMessage sent = ForwarderQueryMessage.WithId(query, newMessageId());

        byte[] queryPacket = DnsCryptEnvelopeCodec.EncryptQuery(
            certificate.EsVersion,
            certificate.ClientMagic,
            clientPublicKey,
            clientSecretKey,
            certificate.ResolverPublicKey,
            clientNonceHalf,
            DnsMessageWriter.Write(sent)
        );

        // Decrypts a packet and checks it answers "sent". Throws CryptographicException or
        // FormatException for a packet that is not an authentic reply, and returns null for an
        // authentic one that answers another question or ID.
        DnsMessage Decode(ReadOnlySpan<byte> packet)
        {
            byte[] plaintext = DnsCryptEnvelopeCodec.DecryptResponse(
                certificate.EsVersion,
                packet,
                clientNonceHalf,
                clientSecretKey,
                certificate.ResolverPublicKey
            );
            DnsMessage reply = DnsMessageReader.Read(plaintext);

            return reply.Id == sent.Id && ForwarderQueryMessage.IsReplyTo(sent, reply)
                ? reply
                : null;
        }

        if (overTcp)
        {
            byte[] responsePacket = await SendTcpAsync(queryPacket, endpoint, stoppingToken);

            try
            {
                return Decode(responsePacket)
                    ?? throw new InvalidDataException(
                        "The DNSCrypt response does not match the query that was sent."
                    );
            }
            catch (Exception exception)
                when (exception is CryptographicException or FormatException)
            {
                throw new InvalidDataException(
                    "The DNSCrypt TCP response could not be decrypted.",
                    exception
                );
            }
        }

        using Socket socket = new(endpoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        await socket.SendToAsync(queryPacket, endpoint, stoppingToken);

        byte[] receiveBuffer = new byte[MaximumDatagramSize];

        while (true)
        {
            SocketReceiveFromResult result = await socket.ReceiveFromAsync(
                receiveBuffer,
                endpoint,
                stoppingToken
            );

            // The socket is unconnected, so anyone can send to it. A datagram from elsewhere,
            // one that fails authentication, or one that answers another query is dropped and the
            // wait continues until the caller's token ends it (RFC 5452 §9.1 for the plain case).
            if (!Equals(result.RemoteEndPoint, endpoint))
                continue;

            if (result.ReceivedBytes >= MaximumDatagramSize)
                return null;

            DnsMessage reply;

            try
            {
                reply = Decode(receiveBuffer.AsSpan(0, result.ReceivedBytes));
            }
            catch (Exception exception)
                when (exception is CryptographicException or FormatException)
            {
                continue;
            }

            if (reply is not null)
                return reply;
        }
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
