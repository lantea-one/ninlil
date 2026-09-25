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
///         Both listeners pass <see cref="DnsTransportProtocolEnum.DnsCrypt" />, so the pipeline
///         treats DNSCrypt over UDP like a stream transport: no UDP truncation, and the subnet
///         limit drops rather than truncates. The encrypted response is not limited to the size
///         of the query.
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
    /// </summary>
    public static async ValueTask HandlePacketAsync(
        ReadOnlyMemory<byte> packet,
        IPAddress remoteAddress,
        DnsTransportProtocolEnum transportProtocol,
        DnsName providerCertificateName,
        DnsQueryProcessingPipeline pipeline,
        IDnsCryptCertificateStore store,
        ILogger logger,
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> sendAsync,
        CancellationToken stoppingToken
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
            return;
        }

        if (!DnsCryptEnvelopeCodec.TryReadClientMagic(packet.Span, out byte[] clientMagic))
            return;

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
            return;
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
            return;
        }

        await pipeline.EnqueueAsync(
            new DnsInboundMessage(
                decryptedQuery,
                transportProtocol,
                remoteAddress,
                (responseBytes, sendToken) =>
                    sendAsync(
                        DnsCryptEnvelopeCodec.EncryptResponse(
                            certificateEntry,
                            clientPublicKey,
                            clientNonceHalf,
                            responseBytes.ToArray()
                        ),
                        sendToken
                    )
            ),
            stoppingToken
        );
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
