using System.Buffers;
using System.Security.Cryptography;

// Define our namespace.
namespace Ninlil.Dns.Server.DnsCrypt;

/// <summary>
///     This class reads and writes the envelopes the DNSCrypt spec puts around an ordinary DNS
///     message: <c>&lt;client-magic&gt; &lt;client-pk&gt; &lt;client-nonce&gt; &lt;encrypted-query&gt;</c>
///     for a query and <c>&lt;resolver-magic&gt; &lt;nonce&gt; &lt;encrypted-response&gt;</c> for
///     a response. The server side (<see cref="TryReadClientMagic" />, <see cref="DecryptQuery" />,
///     <see cref="EncryptResponse" />) is used by <c>DnsCryptQueryHandler</c> in
///     <c>Ninlil.Dns.Protocol.DnsCrypt</c>, and the client side (<see cref="EncryptQuery" />,
///     <see cref="DecryptResponse" />) by the DNSCrypt forwarder client in
///     <c>Ninlil.Dns.Server.Forwarding</c>. Keeping both here lets the wire format be tested
///     without a listener.
/// </summary>
public static class DnsCryptEnvelopeCodec
{
    /// <summary>
    ///     This constant contains the length of a client-magic tag, in bytes.
    /// </summary>
    public const int ClientMagicLength = 8;

    /// <summary>
    ///     This constant contains the length of an X25519 public key, in bytes, for both ciphers.
    /// </summary>
    public const int PublicKeyLength = 32;

    /// <summary>
    ///     This constant contains the length of half the 24-byte nonce. The client chooses and
    ///     sends the first half; the second half is zero bytes for a query and random bytes chosen
    ///     by the resolver for a response.
    /// </summary>
    public const int HalfNonceLength = 12;

    /// <summary>
    ///     This constant contains the authentication tag length, in bytes.
    /// </summary>
    public const int MacLength = 16;

    /// <summary>
    ///     This constant contains the smallest possible client query packet: the envelope fields
    ///     and tag plus at least one byte of padded ciphertext.
    /// </summary>
    public const int MinimumClientQueryLength =
        ClientMagicLength + PublicKeyLength + HalfNonceLength + MacLength + 1;

    /// <summary>
    ///     This constant contains the length a padded client query has at least over UDP, the
    ///     initial <c>&lt;min-query-len&gt;</c> of DNSCrypt v2 section 3. A resolver may truncate
    ///     the answer to a shorter query (section 9), so a client pads to this length.
    /// </summary>
    public const int MinimumUdpPaddedQueryLength = 256;

    /// <summary>
    ///     This constant contains the block size the padded query and response lengths are a
    ///     multiple of (DNSCrypt v2 sections 3, 5 and 8).
    /// </summary>
    public const int PaddingBlockSize = 64;

    /// <summary>
    ///     This constant contains how many blocks beyond the shortest padding a TCP client query
    ///     may add. The shortest padding is 1 to 64 bytes, so up to three more blocks keep the pad
    ///     within the 256 bytes section 5 allows.
    /// </summary>
    public const int MaximumTcpExtraPaddingBlocks = 3;

    /// <summary>
    ///     This field contains the resolver-magic bytes every response starts with, so a client
    ///     can tell a DNSCrypt response from a plain DNS one: <c>"r6fnvWj8"</c> in ASCII.
    /// </summary>
    public static readonly byte[] ResolverMagic = [0x72, 0x36, 0x66, 0x6e, 0x76, 0x57, 0x6a, 0x38];

    /// <summary>
    ///     This method reads the 8-byte client-magic tag from the start of a packet without
    ///     decrypting anything, so the caller can find the certificate (and so the secret key and
    ///     cipher) to decrypt it with. It returns <see langword="false" /> for a packet shorter
    ///     than <see cref="MinimumClientQueryLength" />, which <see cref="DecryptQuery" /> relies on.
    /// </summary>
    public static bool TryReadClientMagic(ReadOnlySpan<byte> packet, out byte[] clientMagic)
    {
        if (packet.Length < MinimumClientQueryLength)
        {
            clientMagic = null;
            return false;
        }

        clientMagic = packet[..ClientMagicLength].ToArray();
        return true;
    }

    /// <summary>
    ///     This method decrypts a client query packet against a specific certificate, returning
    ///     the original, unpadded DNS query message.
    /// </summary>
    /// <param name="certificateEntry">
    ///     The certificate whose client-magic tag matched the packet.
    /// </param>
    /// <param name="packet">
    ///     The complete client query packet, including its client-magic tag. Its length must have
    ///     been checked by <see cref="TryReadClientMagic" />.
    /// </param>
    /// <param name="clientPublicKey">The client's public key, read from the packet.</param>
    /// <param name="clientNonceHalf">The client's half-nonce, read from the packet.</param>
    /// <returns>The decrypted, unpadded DNS query message.</returns>
    /// <exception cref="CryptographicException">
    ///     Thrown when authentication fails: a corrupted or forged packet, or one encrypted with the
    ///     wrong key.
    /// </exception>
    /// <exception cref="FormatException">
    ///     Thrown when the decrypted plaintext does not end in valid padding.
    /// </exception>
    public static byte[] DecryptQuery(
        DnsCryptCertificateEntry certificateEntry,
        ReadOnlySpan<byte> packet,
        out byte[] clientPublicKey,
        out byte[] clientNonceHalf
    )
    {
        clientPublicKey = packet.Slice(ClientMagicLength, PublicKeyLength).ToArray();
        clientNonceHalf = packet
            .Slice(ClientMagicLength + PublicKeyLength, HalfNonceLength)
            .ToArray();
        byte[] encryptedQuery = packet[(ClientMagicLength + PublicKeyLength + HalfNonceLength)..]
            .ToArray();

        // A query's nonce is the client's half followed by 12 zero bytes.
        byte[] nonce = new byte[HalfNonceLength * 2];
        clientNonceHalf.CopyTo(nonce, 0);

        byte[] paddedQuery = DnsCryptCryptoBox.Decrypt(
            certificateEntry.Certificate.EsVersion,
            encryptedQuery,
            nonce,
            clientPublicKey,
            certificateEntry.ResolverSecretKey
        );

        return DnsCryptPadding.Unpad(paddedQuery);
    }

    /// <summary>
    ///     This method encrypts a DNS response into a complete response packet for the client
    ///     that sent the query. It does not limit the packet to the query's size; the caller
    ///     handles size limits.
    /// </summary>
    /// <param name="certificateEntry">The same certificate the originating query was decrypted against.</param>
    /// <param name="clientPublicKey">The same client public key <see cref="DecryptQuery" /> returned.</param>
    /// <param name="clientNonceHalf">The same client half-nonce <see cref="DecryptQuery" /> returned.</param>
    /// <param name="responseMessage">The plaintext, encoded DNS response message.</param>
    public static byte[] EncryptResponse(
        DnsCryptCertificateEntry certificateEntry,
        byte[] clientPublicKey,
        byte[] clientNonceHalf,
        byte[] responseMessage
    )
    {
        byte[] resolverNonceHalf = RandomNumberGenerator.GetBytes(HalfNonceLength);
        byte[] nonce = new byte[HalfNonceLength * 2];
        clientNonceHalf.CopyTo(nonce, 0);
        resolverNonceHalf.CopyTo(nonce, HalfNonceLength);

        // DNSCrypt v2 section 8 requires <resolver-response> <resolver-response-pad> to be a
        // multiple of 64 bytes with a pad (marker included) of 1 to 256 bytes that stays constant
        // for one client nonce; padding to the next 64-byte boundary always gives 1 to 64.
        byte[] paddedResponse = DnsCryptPadding.PadToBlockSize(responseMessage, 64);

        byte[] encryptedResponse = DnsCryptCryptoBox.Encrypt(
            certificateEntry.Certificate.EsVersion,
            paddedResponse,
            nonce,
            clientPublicKey,
            certificateEntry.ResolverSecretKey
        );

        ArrayBufferWriter<byte> buffer = new();
        Write(buffer, ResolverMagic);
        Write(buffer, nonce);
        Write(buffer, encryptedResponse);

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    ///     This method builds a complete client query packet, the client-side counterpart of
    ///     <see cref="DecryptQuery" />. Only the forwarder client uses it. Over UDP the padded
    ///     query is at least <see cref="MinimumUdpPaddedQueryLength" /> bytes (DNSCrypt v2
    ///     section 3). Over TCP the pad is 1 to 256 bytes, chosen at random so the length does not
    ///     identify the query (section 5). Either way the padded length is a multiple of 64 bytes.
    /// </summary>
    /// <param name="esVersion">The cipher the target certificate specifies.</param>
    /// <param name="clientMagic">The target certificate's client-magic tag.</param>
    /// <param name="clientPublicKey">The client's newly generated public key.</param>
    /// <param name="clientSecretKey">The client's newly generated secret key.</param>
    /// <param name="resolverPublicKey">The target certificate's resolver public key.</param>
    /// <param name="clientNonceHalf">A newly generated client half-nonce.</param>
    /// <param name="queryMessage">The plaintext, encoded DNS query message.</param>
    /// <param name="overTcp">Whether the packet is sent over TCP instead of UDP.</param>
    public static byte[] EncryptQuery(
        DnsCryptEsVersion esVersion,
        byte[] clientMagic,
        byte[] clientPublicKey,
        byte[] clientSecretKey,
        byte[] resolverPublicKey,
        byte[] clientNonceHalf,
        byte[] queryMessage,
        bool overTcp
    )
    {
        // A query's nonce is the client's half followed by 12 zero bytes; the resolver fills the
        // second half only in its response.
        byte[] nonce = new byte[HalfNonceLength * 2];
        clientNonceHalf.CopyTo(nonce, 0);

        byte[] paddedQuery = overTcp
            ? DnsCryptPadding.PadWithExtraBlocks(
                queryMessage,
                PaddingBlockSize,
                RandomNumberGenerator.GetInt32(MaximumTcpExtraPaddingBlocks + 1)
            )
            : DnsCryptPadding.PadToMinimumLength(
                queryMessage,
                PaddingBlockSize,
                MinimumUdpPaddedQueryLength
            );

        byte[] encryptedQuery = DnsCryptCryptoBox.Encrypt(
            esVersion,
            paddedQuery,
            nonce,
            resolverPublicKey,
            clientSecretKey
        );

        ArrayBufferWriter<byte> buffer = new();
        Write(buffer, clientMagic);
        Write(buffer, clientPublicKey);
        Write(buffer, clientNonceHalf);
        Write(buffer, encryptedQuery);

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    ///     This method decrypts and authenticates a response packet, the client-side counterpart of
    ///     <see cref="EncryptResponse" />, used by the forwarder client to read the answer to a
    ///     query built with <see cref="EncryptQuery" />.
    /// </summary>
    /// <param name="esVersion">The same cipher <see cref="EncryptQuery" /> was called with.</param>
    /// <param name="packet">The complete response packet, including its resolver-magic tag.</param>
    /// <param name="clientNonceHalf">The same half-nonce <see cref="EncryptQuery" /> was called with.</param>
    /// <param name="clientSecretKey">The same client secret key <see cref="EncryptQuery" /> was called with.</param>
    /// <param name="resolverPublicKey">The same resolver public key <see cref="EncryptQuery" /> was called with.</param>
    /// <returns>The decrypted, unpadded DNS response message.</returns>
    /// <exception cref="FormatException">
    ///     Thrown when <paramref name="packet" /> is too short to be a valid response, does not
    ///     start with the resolver-magic tag, or its echoed nonce half does not match
    ///     <paramref name="clientNonceHalf" />, meaning the response is not for this query.
    /// </exception>
    /// <exception cref="CryptographicException">
    ///     Thrown when authentication fails: a corrupted or forged packet, or one encrypted with the
    ///     wrong key.
    /// </exception>
    public static byte[] DecryptResponse(
        DnsCryptEsVersion esVersion,
        ReadOnlySpan<byte> packet,
        byte[] clientNonceHalf,
        byte[] clientSecretKey,
        byte[] resolverPublicKey
    )
    {
        int nonceOffset = ResolverMagic.Length;
        int encryptedResponseOffset = nonceOffset + HalfNonceLength * 2;

        if (packet.Length < encryptedResponseOffset + MacLength)
            throw new FormatException(
                "The response packet is too short to be a valid DNSCrypt response."
            );

        if (!packet[..ResolverMagic.Length].SequenceEqual(ResolverMagic))
            throw new FormatException(
                "The response packet does not start with the resolver-magic tag."
            );

        byte[] nonce = packet.Slice(nonceOffset, HalfNonceLength * 2).ToArray();

        if (!nonce.AsSpan(0, HalfNonceLength).SequenceEqual(clientNonceHalf))
            throw new FormatException(
                "The response packet's own echoed nonce half does not match this query's own -- it was not meant for this query."
            );

        byte[] encryptedResponse = packet[encryptedResponseOffset..].ToArray();

        byte[] paddedResponse = DnsCryptCryptoBox.Decrypt(
            esVersion,
            encryptedResponse,
            nonce,
            resolverPublicKey,
            clientSecretKey
        );

        return DnsCryptPadding.Unpad(paddedResponse);
    }

    /// <summary>
    ///     This method appends a raw byte sequence to a buffer.
    /// </summary>
    private static void Write(IBufferWriter<byte> buffer, byte[] bytes)
    {
        bytes.CopyTo(buffer.GetSpan(bytes.Length));
        buffer.Advance(bytes.Length);
    }
}
