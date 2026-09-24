using System.Buffers.Binary;
using System.Security.Cryptography;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.DnsCrypt;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsCrypt.Tests;

/// <summary>
///     This class plays the DNSCrypt <em>client</em> role for this test suite: building an
///     encrypted client query packet by hand, and decrypting a server response packet back --
///     the exact wire operations a real DNSCrypt client library performs, kept here so every
///     test method does not have to re-derive the envelope layout itself.
/// </summary>
internal static class DnsCryptWireTestHelper
{
    /// <summary>
    ///     This record carries everything a test needs to remember between building a client
    ///     query packet and later decrypting that query's own response.
    /// </summary>
    public sealed record ClientQuerySession(
        byte[] ClientPublicKey,
        byte[] ClientSecretKey,
        byte[] ClientNonceHalf
    );

    /// <summary>
    ///     This method builds a plain, unencrypted DNS query for <see cref="EchoResolver.QuestionName" />.
    /// </summary>
    public static DnsMessage BuildEchoQuery(ushort id)
    {
        DnsMessage query = new()
        {
            Id = id,
            Opcode = DnsOpcodeEnum.Query,
            RecursionDesired = true,
        };
        query.Questions.Add(
            new DnsQuestion(
                EchoResolver.QuestionName,
                DnsResourceRecordTypeEnum.A,
                DnsClassEnum.Internet
            )
        );
        return query;
    }

    /// <summary>
    ///     This method builds a complete, encrypted DNSCrypt client query packet, per the spec's
    ///     <c>&lt;client-magic&gt; &lt;client-pk&gt; &lt;client-nonce&gt; &lt;encrypted-query&gt;</c>
    ///     structure.
    /// </summary>
    public static (byte[] Packet, ClientQuerySession Session) BuildEncryptedQueryPacket(
        DnsCryptCertificateEntry certificateEntry,
        DnsMessage query
    )
    {
        DnsCryptEsVersion esVersion = certificateEntry.Certificate.EsVersion;
        (byte[] clientPublicKey, byte[] clientSecretKey) = DnsCryptCryptoBox.GenerateKeyPair(
            esVersion
        );
        byte[] clientNonceHalf = RandomNumberGenerator.GetBytes(
            DnsCryptEnvelopeCodec.HalfNonceLength
        );

        byte[] queryNonce = new byte[DnsCryptEnvelopeCodec.HalfNonceLength * 2];
        clientNonceHalf.CopyTo(queryNonce, 0);

        byte[] paddedQuery = DnsCryptPadding.PadToBlockSize(DnsMessageWriter.Write(query), 64);
        byte[] encryptedQuery = DnsCryptCryptoBox.Encrypt(
            esVersion,
            paddedQuery,
            queryNonce,
            certificateEntry.Certificate.ResolverPublicKey,
            clientSecretKey
        );

        byte[] packet =
        [
            .. certificateEntry.Certificate.ClientMagic,
            .. clientPublicKey,
            .. clientNonceHalf,
            .. encryptedQuery,
        ];

        return (packet, new ClientQuerySession(clientPublicKey, clientSecretKey, clientNonceHalf));
    }

    /// <summary>
    ///     This method decrypts a complete DNSCrypt server response packet back into the plain
    ///     DNS message it carries, also returning the raw resolver-magic and echoed client-nonce
    ///     bytes so a test can independently check both against the spec's own requirements.
    /// </summary>
    public static DnsMessage DecryptResponsePacket(
        DnsCryptCertificateEntry certificateEntry,
        ClientQuerySession session,
        byte[] responsePacket,
        out byte[] resolverMagic,
        out byte[] echoedClientNonceHalf
    )
    {
        const int nonceLength = DnsCryptEnvelopeCodec.HalfNonceLength * 2;

        resolverMagic = responsePacket[..DnsCryptEnvelopeCodec.ClientMagicLength];
        byte[] nonce = responsePacket[
            DnsCryptEnvelopeCodec.ClientMagicLength..(
                DnsCryptEnvelopeCodec.ClientMagicLength + nonceLength
            )
        ];
        byte[] encryptedResponse = responsePacket[
            (DnsCryptEnvelopeCodec.ClientMagicLength + nonceLength)..
        ];

        echoedClientNonceHalf = nonce[..DnsCryptEnvelopeCodec.HalfNonceLength];

        byte[] paddedResponse = DnsCryptCryptoBox.Decrypt(
            certificateEntry.Certificate.EsVersion,
            encryptedResponse,
            nonce,
            certificateEntry.Certificate.ResolverPublicKey,
            session.ClientSecretKey
        );

        byte[] responseBytes = DnsCryptPadding.Unpad(paddedResponse);
        return DnsMessageReader.Read(responseBytes);
    }

    /// <summary>
    ///     This method scans a raw certificate-discovery UDP response for every embedded 124-byte
    ///     DNSCrypt certificate, by its own <c>"DNSC"</c> magic -- deliberately not via
    ///     <see cref="DnsMessageReader" />, since this codebase's own <see cref="DnsMessageReader" />
    ///     corrupts a TXT record's arbitrary binary content on read (a real, known gap),
    ///     the same workaround this project's own DNSCrypt smoke test needed.
    /// </summary>
    public static List<byte[]> ExtractCertificatesFromDiscoveryResponse(byte[] rawResponse)
    {
        List<byte[]> certificates = [];
        byte[] magic = "DNSC"u8.ToArray();

        for (int i = 0; i <= rawResponse.Length - 124; i++)
        {
            if (rawResponse.AsSpan(i, 4).SequenceEqual(magic))
                certificates.Add(rawResponse[i..(i + 124)]);
        }

        return certificates;
    }

    /// <summary>
    ///     This method decodes the fields of one raw, extracted 124-byte certificate needed to
    ///     independently re-verify its own Ed25519 signature: the exact bytes that were signed,
    ///     and the signature itself.
    /// </summary>
    public static (
        byte[] SigningInput,
        byte[] Signature,
        DnsCryptEsVersion EsVersion
    ) DecodeCertificateForVerification(byte[] certificate)
    {
        DnsCryptEsVersion esVersion = (DnsCryptEsVersion)
            BinaryPrimitives.ReadUInt16BigEndian(certificate.AsSpan(4, 2));
        byte[] signature = certificate[8..72];
        byte[] signingInput = certificate[72..124];

        return (signingInput, signature, esVersion);
    }
}
