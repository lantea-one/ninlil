using System.Buffers.Binary;
using System.Security.Cryptography;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.DnsCrypt;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsCrypt.Tests;

/// <summary>
///     This class plays the DNSCrypt <em>client</em> for these tests: it builds encrypted query
///     packets and decrypts response packets, as a DNSCrypt client library would, so each test
///     does not have to repeat the envelope layout.
/// </summary>
internal static class DnsCryptWireTestHelper
{
    /// <summary>
    ///     This record carries what a test needs to keep between building a client query packet
    ///     and decrypting its response.
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
    ///     This method builds a plain A query for <paramref name="name" />. When
    ///     <paramref name="ednsPayloadSize" /> is set, the query carries an OPT record that
    ///     advertises it; without one the client accepts only 512 bytes over UDP.
    /// </summary>
    public static DnsMessage BuildQuery(ushort id, DnsName name, ushort? ednsPayloadSize = null)
    {
        DnsMessage query = new()
        {
            Id = id,
            Opcode = DnsOpcodeEnum.Query,
            RecursionDesired = true,
        };
        query.Questions.Add(
            new DnsQuestion(name, DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet)
        );

        if (ednsPayloadSize is { } payloadSize)
            query.Edns = new DnsEdnsOptions(0, payloadSize, false, []);

        return query;
    }

    /// <summary>
    ///     This method builds an encrypted DNSCrypt client query packet, following the spec's
    ///     <c>&lt;client-magic&gt; &lt;client-pk&gt; &lt;client-nonce&gt; &lt;encrypted-query&gt;</c>
    ///     structure. <paramref name="minimumPaddedLength" /> makes the client pad the plaintext
    ///     to at least that many bytes, as a client does to signal that it accepts a large
    ///     response; the packet is then that length plus 68 bytes of envelope and MAC.
    /// </summary>
    public static (byte[] Packet, ClientQuerySession Session) BuildEncryptedQueryPacket(
        DnsCryptCertificateEntry certificateEntry,
        DnsMessage query,
        int minimumPaddedLength = 0
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

        // Zero bytes after the padding marker are still valid ISO/IEC 7816-4 padding.
        if (paddedQuery.Length < minimumPaddedLength)
            Array.Resize(ref paddedQuery, minimumPaddedLength);

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
    ///     This method decrypts a DNSCrypt server response packet into the DNS message it carries,
    ///     and also returns the resolver-magic and echoed client-nonce bytes so a test can check
    ///     both against the spec.
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
    ///     This method scans a raw certificate discovery response for every embedded 124-byte
    ///     DNSCrypt certificate by its <c>"DNSC"</c> magic, without parsing the message with
    ///     <see cref="DnsMessageReader" />.
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
    ///     This method takes from one extracted 124-byte certificate what is needed to verify its
    ///     Ed25519 signature: the signed bytes and the signature.
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
