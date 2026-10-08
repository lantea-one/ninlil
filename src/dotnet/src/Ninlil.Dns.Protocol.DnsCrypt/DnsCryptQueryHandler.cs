using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.DnsCrypt;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsCrypt;

/// <summary>
///     This class makes the routing decision both DNSCrypt listeners need: is a packet a
///     plaintext certificate discovery query (answered here), or an encrypted client query
///     (decrypted, enqueued onto <see cref="DnsQueryProcessingPipeline" /> like any other
///     transport's query, and its answer encrypted before it is sent back)?
///     <see cref="DnsCryptUdpListener" /> and <see cref="DnsCryptTcpListener" /> share it; they
///     differ only in how they receive and send bytes.
///     <para>
///         Both listeners pass <see cref="DnsTransportProtocolEnum.DnsCrypt" /> as the protocol,
///         so the query log reports one DNSCrypt transport. A datagram listener also marks each
///         query as a datagram and caps its reply to the size of the query packet (see
///         <see cref="MaxPlaintextResponseLength" />), so the pipeline truncates an answer that
///         would not fit and the encrypted reply is never larger than the query, as the DNSCrypt
///         spec requires. A stream query has no cap.
///     </para>
/// </summary>
internal static class DnsCryptQueryHandler
{
    /// <summary>
    ///     This method routes one received packet. A certificate discovery query is answered at
    ///     once. Anything else is treated as an encrypted client query: it is decrypted with the
    ///     tracked certificate its client-magic names and, if that succeeds, enqueued onto
    ///     <paramref name="pipeline" /> with a response delegate that encrypts the answer before
    ///     passing it to <paramref name="sendAsync" />. A packet that is neither is dropped
    ///     without a reply: a malformed plaintext query or a query that cannot be decrypted (an
    ///     unknown client-magic, corrupted or forged ciphertext) has no reliable ID to answer, and
    ///     answering unknown senders would help reflection attacks.
    ///     <para>
    ///         A stream listener that limits queries in flight passes
    ///         <paramref name="queryResponder" /> and <paramref name="onDropped" />: the encrypted
    ///         answer of a pipeline query goes to <paramref name="queryResponder" /> and a dropped
    ///         query is reported to <paramref name="onDropped" />, while a discovery answer still
    ///         goes to <paramref name="sendAsync" />. A datagram listener omits both and every
    ///         answer goes to <paramref name="sendAsync" />.
    ///     </para>
    /// </summary>
    /// <returns>
    ///     <see langword="true" /> when the packet was enqueued onto the pipeline, so that exactly
    ///     one of <paramref name="queryResponder" /> and <paramref name="onDropped" /> will be
    ///     called; <see langword="false" /> when the handler finished with the packet itself.
    /// </returns>
    public static async ValueTask<bool> HandlePacketAsync(
        ReadOnlyMemory<byte> packet,
        IPAddress remoteAddress,
        DnsTransportProtocolEnum transportProtocol,
        bool isDatagram,
        DnsName providerCertificateName,
        DnsQueryProcessingPipeline pipeline,
        IDnsCryptCertificateStore store,
        ILogger logger,
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> sendAsync,
        CancellationToken stoppingToken,
        DnsResponseSenderAsync queryResponder = null,
        DnsQueryDropNotifier onDropped = null
    )
    {
        if (
            TryHandleCertificateDiscovery(
                packet,
                providerCertificateName,
                store,
                out byte[] discoveryResponse
            )
        )
        {
            await sendAsync(discoveryResponse, stoppingToken);
            return false;
        }

        if (!DnsCryptEnvelopeCodec.TryReadClientMagic(packet.Span, out byte[] clientMagic))
            return false;

        DnsCryptCertificateEntry certificateEntry = FindCertificateByClientMagic(
            store,
            clientMagic
        );

        if (certificateEntry is null)
        {
            logger.LogDebug(
                "Dropping a DNSCrypt packet from {RemoteAddress} whose client-magic tag does not match any currently tracked certificate.",
                remoteAddress
            );
            return false;
        }

        byte[] decryptedQuery;
        byte[] clientPublicKey;
        byte[] clientNonceHalf;

        try
        {
            decryptedQuery = DnsCryptEnvelopeCodec.DecryptQuery(
                certificateEntry,
                packet.Span,
                out clientPublicKey,
                out clientNonceHalf
            );
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            logger.LogDebug(
                exception,
                "Dropping an undecryptable DNSCrypt packet from {RemoteAddress}.",
                remoteAddress
            );
            return false;
        }

        await pipeline.EnqueueAsync(
            new DnsInboundMessage(
                decryptedQuery,
                transportProtocol,
                remoteAddress,
                (responseBytes, sendToken) =>
                {
                    byte[] encrypted = DnsCryptEnvelopeCodec.EncryptResponse(
                        certificateEntry,
                        clientPublicKey,
                        clientNonceHalf,
                        responseBytes.ToArray()
                    );

                    return queryResponder is not null
                        ? queryResponder(encrypted, sendToken)
                        : sendAsync(encrypted, sendToken);
                }
            )
            {
                OnDropped = onDropped,
                IsDatagram = isDatagram,
                MaxResponseOctets = isDatagram ? MaxPlaintextResponseLength(packet.Length) : null,
            },
            stoppingToken
        );

        return true;
    }

    /// <summary>
    ///     This method returns the largest plaintext DNS response that
    ///     <see cref="DnsCryptEnvelopeCodec.EncryptResponse" /> turns into a packet of at most
    ///     <paramref name="queryPacketLength" /> bytes. The DNSCrypt spec forbids a response
    ///     packet larger than the query packet, so a longer answer must be truncated (TC) and the
    ///     client retries over TCP.
    ///     <para>
    ///         The arithmetic, for a query packet of L bytes: a response packet is the resolver
    ///         magic (8) + the nonce (24) + the MAC (16) + the padded plaintext, so the envelope
    ///         costs 48 bytes. A plaintext of n bytes is padded to the next multiple of 64 with
    ///         at least one padding byte, which is 64 * (floor(n / 64) + 1) bytes. The packet fits
    ///         when 64 * (floor(n / 64) + 1) &lt;= L - 48, that is when
    ///         floor(n / 64) + 1 &lt;= floor((L - 48) / 64), so the largest n is
    ///         64 * floor((L - 48) / 64) - 1. For L = 256 that is 64 * 3 - 1 = 191, which encrypts
    ///         to 8 + 24 + 192 + 16 = 240 bytes.
    ///     </para>
    /// </summary>
    /// <returns>
    ///     The plaintext cap, or 0 when the query packet is too short for any response to fit (the
    ///     pipeline then drops the answer instead of sending an oversized packet).
    /// </returns>
    internal static int MaxPlaintextResponseLength(int queryPacketLength)
    {
        // DnsCryptEnvelopeCodec.EncryptResponse pads to a multiple of this block size.
        const int blockSize = 64;
        int envelopeOverhead =
            DnsCryptEnvelopeCodec.ResolverMagic.Length
            + DnsCryptEnvelopeCodec.HalfNonceLength * 2
            + DnsCryptEnvelopeCodec.MacLength;

        int paddedBlocks = (queryPacketLength - envelopeOverhead) / blockSize;

        return Math.Max(0, paddedBlocks * blockSize - 1);
    }

    /// <summary>
    ///     This method tries to decode a packet as a plaintext certificate discovery query and, if
    ///     it is one, builds the encoded response.
    /// </summary>
    private static bool TryHandleCertificateDiscovery(
        ReadOnlyMemory<byte> packet,
        DnsName providerCertificateName,
        IDnsCryptCertificateStore store,
        out byte[] response
    )
    {
        DnsMessage request;

        try
        {
            request = DnsMessageReader.Read(packet.Span);
        }
        catch (FormatException)
        {
            // Not a plaintext DNS message, so most likely an encrypted client query, which the
            // caller tries next.
            response = null;
            return false;
        }

        if (
            !DnsCryptCertificateDiscoveryResponder.TryBuildResponse(
                request,
                providerCertificateName,
                store,
                out DnsMessage responseMessage
            )
        )
        {
            response = null;
            return false;
        }

        response = DnsMessageWriter.Write(responseMessage);
        return true;
    }

    /// <summary>
    ///     This method finds the tracked certificate a client-magic tag names, if any. It does not
    ///     check the certificate's validity period.
    /// </summary>
    private static DnsCryptCertificateEntry FindCertificateByClientMagic(
        IDnsCryptCertificateStore store,
        byte[] clientMagic
    )
    {
        foreach (DnsCryptCertificateEntry entry in store.Entries)
            if (entry.Certificate.ClientMagic.AsSpan().SequenceEqual(clientMagic))
                return entry;

        return null;
    }
}
