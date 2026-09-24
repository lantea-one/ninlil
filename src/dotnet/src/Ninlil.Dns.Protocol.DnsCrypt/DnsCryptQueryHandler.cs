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
///     This class implements the one packet-routing decision every DNSCrypt transport (UDP and
///     TCP) needs to make identically: is this an ordinary, plaintext certificate discovery
///     query (answered directly), or an encrypted client query (decrypted, enqueued onto the
///     shared <see cref="DnsQueryProcessingPipeline" /> exactly like any other transport's own
///     query, then its eventual answer re-encrypted before being sent back)? Kept here, shared
///     by both <see cref="DnsCryptUdpListener" /> and <see cref="DnsCryptTcpListener" />, rather
///     than duplicated -- the two listeners differ only in how they receive and send raw bytes.
/// </summary>
internal static class DnsCryptQueryHandler
{
    /// <summary>
    ///     This method routes a single received packet: a certificate discovery query is
    ///     answered directly; anything else is treated as an encrypted client query, decrypted
    ///     against whichever tracked certificate its client-magic tag names, and (if that
    ///     succeeds) enqueued onto <paramref name="pipeline" /> with a response delegate that
    ///     re-encrypts the eventual answer before handing it to <paramref name="sendAsync" />. A
    ///     packet that fails to parse as either shape is silently dropped -- both a malformed
    ///     plaintext query and a client query this resolver cannot decrypt (wrong/expired
    ///     certificate, corrupted or forged ciphertext) have no reliable identifier a useful
    ///     error response could even be addressed to, and answering strangers at all invites use
    ///     as a reflection/amplification vector.
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
    ///     This method tries to decode a packet as a plaintext certificate discovery query, and
    ///     if it is one, builds its already-encoded response.
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
            // Not a valid plaintext DNS message at all -- almost certainly an encrypted client
            // query instead, which is exactly what the caller tries next.
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
    ///     This method finds the currently tracked certificate a client-magic tag names, if any.
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
