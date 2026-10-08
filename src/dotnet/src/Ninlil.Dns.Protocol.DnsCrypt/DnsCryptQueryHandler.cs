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
            },
            stoppingToken
        );

        return true;
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
