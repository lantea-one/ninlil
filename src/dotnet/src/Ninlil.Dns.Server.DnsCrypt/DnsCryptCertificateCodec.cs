using System.Buffers;
using System.Buffers.Binary;
using System.Text;

// Define our namespace.
namespace Ninlil.Dns.Server.DnsCrypt;

/// <summary>
///     This class encodes a <see cref="DnsCryptCertificate" /> into, and (for
///     <c>Ninlil.Dns.Server.Forwarding</c>'s client role) decodes one back out of, the exact
///     124-byte wire layout the DNSCrypt spec defines:
///     <c>&lt;cert-magic&gt; &lt;es-version&gt; &lt;protocol-minor-version&gt; &lt;signature&gt;
///     &lt;resolver-pk&gt; &lt;client-magic&gt; &lt;serial&gt; &lt;ts-start&gt; &lt;ts-end&gt;
///     &lt;extensions&gt;</c> (no extensions are ever emitted or expected, but the field must
///     still be accounted for in what gets signed).
/// </summary>
public static class DnsCryptCertificateCodec
{
    /// <summary>
    ///     This constant contains the certificate's own magic bytes, <c>"DNSC"</c>.
    /// </summary>
    private static readonly byte[] CertMagic = Encoding.ASCII.GetBytes("DNSC");

    /// <summary>
    ///     This constant contains the protocol minor version this project always writes.
    /// </summary>
    private const ushort ProtocolMinorVersion = 0x0000;

    /// <summary>
    ///     This constant contains a fully encoded certificate's own fixed total length: 4
    ///     (magic) + 2 (es-version) + 2 (minor version) + 64 (Ed25519 signature) + 32
    ///     (resolver public key) + 8 (client-magic) + 4 (serial) + 4 (ts-start) + 4 (ts-end).
    /// </summary>
    public const int EncodedLength = 124;

    /// <summary>
    ///     This method encodes a fully signed certificate into its complete wire form.
    /// </summary>
    public static byte[] Encode(DnsCryptCertificate certificate)
    {
        ArrayBufferWriter<byte> buffer = new();

        WriteBytes(buffer, CertMagic);
        WriteUInt16(buffer, (ushort)certificate.EsVersion);
        WriteUInt16(buffer, ProtocolMinorVersion);
        WriteBytes(buffer, certificate.Signature);
        WriteBytes(buffer, BuildSignedData(certificate));

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    ///     This method decodes a certificate's complete wire form back into a
    ///     <see cref="DnsCryptCertificate" /> -- the mirror of <see cref="Encode" />, needed by a
    ///     client (never by this resolver's own server role, which only ever writes its own
    ///     certificates) to parse one discovered from an upstream resolver's own TXT response.
    ///     This does not itself verify the embedded <see cref="DnsCryptCertificate.Signature" />
    ///     against any key -- a caller must do that separately, against whichever public key it
    ///     already trusts the certificate's provider to use (an <c>sdns://</c> stamp's own
    ///     embedded key, for this project's forwarder client).
    /// </summary>
    /// <exception cref="FormatException">
    ///     Thrown when <paramref name="wireBytes" /> is not exactly <see cref="EncodedLength" />
    ///     bytes, or does not start with the certificate magic <c>"DNSC"</c>.
    /// </exception>
    public static DnsCryptCertificate Decode(ReadOnlySpan<byte> wireBytes)
    {
        if (wireBytes.Length != EncodedLength)
            throw new FormatException(
                $"A DNSCrypt certificate must be exactly {EncodedLength} bytes; got {wireBytes.Length}."
            );

        if (!wireBytes[..4].SequenceEqual(CertMagic))
            throw new FormatException("The certificate does not start with the 'DNSC' magic.");

        DnsCryptEsVersion esVersion = (DnsCryptEsVersion)
            BinaryPrimitives.ReadUInt16BigEndian(wireBytes[4..6]);
        // Bytes 6..8 are the protocol minor version -- this project has never emitted anything
        // other than 0x0000 and does not need to branch on it to decode the rest.
        byte[] signature = wireBytes[8..72].ToArray();
        byte[] resolverPublicKey = wireBytes[72..104].ToArray();
        byte[] clientMagic = wireBytes[104..112].ToArray();
        uint serial = BinaryPrimitives.ReadUInt32BigEndian(wireBytes[112..116]);
        DateTimeOffset validFrom = DateTimeOffset.FromUnixTimeSeconds(
            BinaryPrimitives.ReadUInt32BigEndian(wireBytes[116..120])
        );
        DateTimeOffset validUntil = DateTimeOffset.FromUnixTimeSeconds(
            BinaryPrimitives.ReadUInt32BigEndian(wireBytes[120..124])
        );

        return new DnsCryptCertificate(
            esVersion,
            resolverPublicKey,
            clientMagic,
            serial,
            validFrom,
            validUntil,
            signature
        );
    }

    /// <summary>
    ///     This method builds the exact byte sequence the DNSCrypt spec defines as what gets
    ///     Ed25519-signed by the provider's own long-term key: every field except
    ///     <see cref="DnsCryptCertificate.Signature" /> itself (which obviously cannot be part
    ///     of its own input) -- <c>&lt;resolver-pk&gt; &lt;client-magic&gt; &lt;serial&gt;
    ///     &lt;ts-start&gt; &lt;ts-end&gt; &lt;extensions&gt;</c>.
    /// </summary>
    public static byte[] BuildSignedData(DnsCryptCertificate certificate)
    {
        ArrayBufferWriter<byte> buffer = new();

        WriteBytes(buffer, certificate.ResolverPublicKey);
        WriteBytes(buffer, certificate.ClientMagic);
        WriteUInt32(buffer, certificate.Serial);
        WriteUInt32(buffer, (uint)certificate.ValidFrom.ToUnixTimeSeconds());
        WriteUInt32(buffer, (uint)certificate.ValidUntil.ToUnixTimeSeconds());
        // No extensions are ever emitted.

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    ///     This method appends a raw byte sequence to a buffer.
    /// </summary>
    private static void WriteBytes(IBufferWriter<byte> buffer, byte[] bytes)
    {
        bytes.CopyTo(buffer.GetSpan(bytes.Length));
        buffer.Advance(bytes.Length);
    }

    /// <summary>
    ///     This method writes a big-endian 16-bit value.
    /// </summary>
    private static void WriteUInt16(IBufferWriter<byte> buffer, ushort value)
    {
        Span<byte> span = buffer.GetSpan(2);
        BinaryPrimitives.WriteUInt16BigEndian(span, value);
        buffer.Advance(2);
    }

    /// <summary>
    ///     This method writes a big-endian 32-bit value.
    /// </summary>
    private static void WriteUInt32(IBufferWriter<byte> buffer, uint value)
    {
        Span<byte> span = buffer.GetSpan(4);
        BinaryPrimitives.WriteUInt32BigEndian(span, value);
        buffer.Advance(4);
    }
}
