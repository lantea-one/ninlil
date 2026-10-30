using System.Buffers;
using System.Buffers.Binary;
using System.Text;

// Define our namespace.
namespace Ninlil.Dns.Server.DnsCrypt;

/// <summary>
///     This class encodes a <see cref="DnsCryptCertificate" /> into the 124-byte wire layout the
///     DNSCrypt v2 specification defines (section 12), and decodes one for the DNSCrypt forwarder client in
///     <c>Ninlil.Dns.Server.Forwarding</c>:
///     <c>&lt;cert-magic&gt; &lt;es-version&gt; &lt;protocol-minor-version&gt; &lt;signature&gt;
///     &lt;resolver-pk&gt; &lt;client-magic&gt; &lt;serial&gt; &lt;ts-start&gt; &lt;ts-end&gt;
///     &lt;extensions&gt;</c>. Extensions are never written or read, so they are empty in the
///     signed data too.
/// </summary>
public static class DnsCryptCertificateCodec
{
    /// <summary>
    ///     This field contains the certificate magic bytes, <c>"DNSC"</c>.
    /// </summary>
    private static readonly byte[] CertMagic = Encoding.ASCII.GetBytes("DNSC");

    /// <summary>
    ///     This constant contains the protocol minor version Ninlil always writes.
    /// </summary>
    private const ushort ProtocolMinorVersion = 0x0000;

    /// <summary>
    ///     This constant contains the fixed length of an encoded certificate: 4
    ///     (magic) + 2 (es-version) + 2 (minor version) + 64 (Ed25519 signature) + 32
    ///     (resolver public key) + 8 (client-magic) + 4 (serial) + 4 (ts-start) + 4 (ts-end).
    /// </summary>
    public const int EncodedLength = 124;

    /// <summary>
    ///     This method encodes a signed certificate into its wire form.
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
    ///     This method decodes a certificate's wire form, the reverse of <see cref="Encode" />. Only
    ///     the client side needs it, to parse a certificate from an upstream resolver's TXT
    ///     answer. It does not verify <see cref="DnsCryptCertificate.Signature" />; the caller must
    ///     check it against the provider key it trusts (for the forwarder client, the key in the
    ///     <c>sdns://</c> stamp).
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
        // Bytes 6..8 are the protocol minor version, which does not change the layout, so it is
        // not read.
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
    ///     This method builds the bytes the provider's long-term Ed25519 key signs, as the DNSCrypt
    ///     spec defines them: <c>&lt;resolver-pk&gt; &lt;client-magic&gt; &lt;serial&gt;
    ///     &lt;ts-start&gt; &lt;ts-end&gt; &lt;extensions&gt;</c>. The same bytes follow the
    ///     signature in the encoded certificate.
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
