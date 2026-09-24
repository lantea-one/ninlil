using System.Buffers;
using System.Security.Cryptography;

// Define our namespace.
namespace Ninlil.Dns.Server.DnsCrypt;

/// <summary>
///     This class reads and writes the client-query and server-response wire envelopes the
///     DNSCrypt spec defines around an otherwise ordinary, plaintext DNS message:
///     <c>&lt;client-magic&gt; &lt;client-pk&gt; &lt;client-nonce&gt; &lt;encrypted-query&gt;</c>
///     for a query, and <c>&lt;resolver-magic&gt; &lt;nonce&gt; &lt;encrypted-response&gt;</c> for
///     a response -- both ends of the exchange <c>Ninlil.Dns.Protocol.DnsCrypt</c>'s
///     listener needs, kept here rather than in the transport project itself so this wire format
///     stays independently testable, the same relationship
///     <see cref="Ninlil.Dns.Server.Dnssec" />'s signature verification already has to the
///     authoritative and recursive engines that both depend on it.
/// </summary>
public static class DnsCryptEnvelopeCodec
{
    /// <summary>
    ///     This constant contains a client-magic tag's own length, in bytes.
    /// </summary>
    public const int ClientMagicLength = 8;

    /// <summary>
    ///     This constant contains an X25519 public key's own length, in bytes -- the same for
    ///     both currently supported ciphers.
    /// </summary>
    public const int PublicKeyLength = 32;

    /// <summary>
    ///     This constant contains half of this construction's own 24-byte nonce -- the half a
    ///     client chooses and sends explicitly; the other half is chosen by whichever side
    ///     completes the nonce (all zero bytes for a query, the resolver's own random bytes for
    ///     a response).
    /// </summary>
    public const int HalfNonceLength = 12;

    /// <summary>
    ///     This constant contains this construction's own authentication tag length, in bytes.
    /// </summary>
    public const int MacLength = 16;

    /// <summary>
    ///     This constant contains the smallest a client query packet could possibly be: every
    ///     envelope field's own minimum length, plus at least one byte of actual (padded)
    ///     ciphertext.
    /// </summary>
    public const int MinimumClientQueryLength =
        ClientMagicLength + PublicKeyLength + HalfNonceLength + MacLength + 1;

    /// <summary>
    ///     This constant contains the resolver-magic bytes a server response is always prefixed
    ///     with, letting a client immediately tell a DNSCrypt response apart from an ordinary
    ///     plaintext one -- <c>"r6fnvWj8"</c> in ASCII.
    /// </summary>
    public static readonly byte[] ResolverMagic = [0x72, 0x36, 0x66, 0x6e, 0x76, 0x57, 0x6a, 0x38];

    /// <summary>
    ///     This method reads the 8-byte client-magic tag off the front of a packet, without
    ///     attempting to decrypt anything -- enough to look up which tracked certificate (and so
    ///     which resolver secret key and cipher) the rest of the packet should be decrypted
    ///     against.
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
    ///     The tracked certificate whose own resolver secret key and cipher this packet's
    ///     client-magic tag selected.
    /// </param>
    /// <param name="packet">The complete client query packet, including its client-magic tag.</param>
    /// <param name="clientPublicKey">The client's own public key, read out of the packet.</param>
    /// <param name="clientNonceHalf">The client's own half-nonce, read out of the packet.</param>
    /// <returns>The decrypted, unpadded DNS query message.</returns>
    /// <exception cref="CryptographicException">
    ///     Thrown when authentication fails -- a corrupted, forged, or wrong-key-encrypted packet.
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

        // A query's own nonce is the client's chosen half followed by 12 NUL bytes -- the
        // client-side counterpart to a response's nonce, which instead completes the second
        // half with the resolver's own random bytes.
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
    ///     This method encrypts a resolver's own DNS response message back into a complete
    ///     response packet, ready to send back to the client that sent the query it answers.
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

        // Per the spec, the total length of <resolver-response> <resolver-response-pad> must be
        // a multiple of 64 bytes, with the pad itself (including its own leading marker byte)
        // between 1 and 256 bytes -- exactly what padding to the next 64-byte boundary always
        // produces.
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
    ///     This method builds a complete client query packet -- the mirror of
    ///     <see cref="EncryptResponse" />, needed by a client role (never by this resolver's own
    ///     server role) to actually send an encrypted query.
    /// </summary>
    /// <param name="esVersion">The cipher the target certificate itself specifies.</param>
    /// <param name="clientMagic">The target certificate's own client-magic tag.</param>
    /// <param name="clientPublicKey">The client's own, freshly generated public key.</param>
    /// <param name="clientSecretKey">The client's own, freshly generated secret key.</param>
    /// <param name="resolverPublicKey">The target certificate's own resolver public key.</param>
    /// <param name="clientNonceHalf">A freshly generated, client-chosen half-nonce.</param>
    /// <param name="queryMessage">The plaintext, encoded DNS query message.</param>
    public static byte[] EncryptQuery(
        DnsCryptEsVersion esVersion,
        byte[] clientMagic,
        byte[] clientPublicKey,
        byte[] clientSecretKey,
        byte[] resolverPublicKey,
        byte[] clientNonceHalf,
        byte[] queryMessage
    )
    {
        // A query's own nonce is the client's chosen half followed by 12 NUL bytes -- the
        // resolver completes the second half only for its own response, never for the query
        // itself.
        byte[] nonce = new byte[HalfNonceLength * 2];
        clientNonceHalf.CopyTo(nonce, 0);

        byte[] paddedQuery = DnsCryptPadding.PadToBlockSize(queryMessage, 64);

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
    ///     This method decrypts and authenticates a complete server response packet -- the
    ///     mirror of <see cref="DecryptQuery" />, needed by a client role to read back what
    ///     <see cref="EncryptQuery" /> queried for.
    /// </summary>
    /// <param name="esVersion">The same cipher <see cref="EncryptQuery" /> was called with.</param>
    /// <param name="packet">The complete response packet, including its resolver-magic tag.</param>
    /// <param name="clientNonceHalf">The same half-nonce <see cref="EncryptQuery" /> was called with.</param>
    /// <param name="clientSecretKey">The same client secret key <see cref="EncryptQuery" /> was called with.</param>
    /// <param name="resolverPublicKey">The same resolver public key <see cref="EncryptQuery" /> was called with.</param>
    /// <returns>The decrypted, unpadded DNS response message.</returns>
    /// <exception cref="FormatException">
    ///     Thrown when <paramref name="packet" /> is too short to be a valid response, does not
    ///     start with the resolver-magic tag, or its own echoed nonce half does not match
    ///     <paramref name="clientNonceHalf" /> -- a sign this response was not actually meant for
    ///     this query.
    /// </exception>
    /// <exception cref="CryptographicException">
    ///     Thrown when authentication fails -- a corrupted, forged, or wrong-key-encrypted packet.
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
