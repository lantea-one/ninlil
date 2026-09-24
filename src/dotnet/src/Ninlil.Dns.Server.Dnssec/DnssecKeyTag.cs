using System.Buffers.Binary;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Dnssec;

/// <summary>
///     This class computes a <see cref="Common.Wire.Records.DnsKeyRecordData" />'s key tag, per
///     RFC 4034 Appendix B -- the short identifier <see cref="Common.Wire.Records.DsRecordData.KeyTag" />
///     and <see cref="Common.Wire.Records.RrSigRecordData.KeyTag" /> both carry, letting a
///     validator narrow down which key among several signed something without needing to try
///     every one.
/// </summary>
public static class DnssecKeyTag
{
    /// <summary>
    ///     This method computes a DNSKEY's key tag from its full RDATA.
    /// </summary>
    /// <param name="algorithm">The key's own algorithm, read from the same RDATA.</param>
    /// <param name="dnsKeyRdata">
    ///     The complete DNSKEY RDATA -- Flags, Protocol, Algorithm, and Public Key, exactly as
    ///     <see cref="Common.Wire.Records.DnsKeyRecordDataCodec" /> would write it.
    /// </param>
    /// <returns>The computed key tag.</returns>
    public static ushort Compute(DnssecAlgorithmEnum algorithm, byte[] dnsKeyRdata) =>
        algorithm == DnssecAlgorithmEnum.RsaMd5
            ? ComputeRsaMd5SpecialCase(dnsKeyRdata)
            : ComputeGeneric(dnsKeyRdata);

    /// <summary>
    ///     This method implements RFC 4034 Appendix B's general algorithm: sum the RDATA's own
    ///     bytes as a series of big-endian 16-bit groups, ignoring carry beyond 16 bits.
    /// </summary>
    private static ushort ComputeGeneric(byte[] dnsKeyRdata)
    {
        uint accumulator = 0;

        for (int i = 0; i < dnsKeyRdata.Length; i++)
            accumulator += (i & 1) == 1 ? dnsKeyRdata[i] : (uint)(dnsKeyRdata[i] << 8);

        accumulator += (accumulator >> 16) & 0xFFFF;

        return (ushort)(accumulator & 0xFFFF);
    }

    /// <summary>
    ///     This method implements RFC 4034 Appendix B.1's special case for algorithm 1
    ///     (RSA/MD5): the most significant 16 bits of the least significant 24 bits of the
    ///     public key modulus -- the 4th-to-last and 3rd-to-last octets of the modulus.
    /// </summary>
    private static ushort ComputeRsaMd5SpecialCase(byte[] dnsKeyRdata)
    {
        // Flags (2) + Protocol (1) + Algorithm (1) precede the RFC 3110 public key field.
        byte[] publicKey = dnsKeyRdata[4..];

        int exponentLength;
        int offset;

        // RFC 3110 §2 encodes the exponent length as either one byte, or (when that byte is
        // zero, signaling an exponent too long to fit in one byte) a following big-endian
        // 16-bit length -- so the length field's own width has to be determined before the
        // exponent itself can be located.
        if (publicKey[0] == 0)
        {
            exponentLength = BinaryPrimitives.ReadUInt16BigEndian(publicKey.AsSpan(1, 2));
            offset = 3;
        }
        else
        {
            exponentLength = publicKey[0];
            offset = 1;
        }

        byte[] modulus = publicKey[(offset + exponentLength)..];

        return (ushort)((modulus[^3] << 8) | modulus[^2]);
    }
}
