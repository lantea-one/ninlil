using System.Buffers.Binary;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Dnssec;

/// <summary>
///     This class computes a DNSKEY's key tag per RFC 4034 Appendix B. The tag is the short
///     identifier carried in <see cref="Common.Wire.Records.DsRecordData.KeyTag" /> and
///     <see cref="Common.Wire.Records.RrSigRecordData.KeyTag" />, so a validator can pick the
///     right key without trying each one.
/// </summary>
public static class DnssecKeyTag
{
    /// <summary>
    ///     This method computes a DNSKEY's key tag from its full RDATA.
    /// </summary>
    /// <param name="algorithm">The key's algorithm, as in the RDATA.</param>
    /// <param name="dnsKeyRdata">
    ///     The complete DNSKEY RDATA (Flags, Protocol, Algorithm and Public Key), as
    ///     <see cref="Common.Wire.Records.DnsKeyRecordDataCodec" /> writes it.
    /// </param>
    /// <returns>The computed key tag.</returns>
    public static ushort Compute(DnssecAlgorithmEnum algorithm, byte[] dnsKeyRdata) =>
        algorithm == DnssecAlgorithmEnum.RsaMd5
            ? ComputeRsaMd5SpecialCase(dnsKeyRdata)
            : ComputeGeneric(dnsKeyRdata);

    /// <summary>
    ///     This method implements the general algorithm of RFC 4034 Appendix B: sum the RDATA as
    ///     big-endian 16-bit words, add the carry above 16 bits back in once, and keep the low
    ///     16 bits.
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
    ///     This method implements the RFC 4034 Appendix B.1 special case for algorithm 1
    ///     (RSA/MD5): the most significant 16 bits of the least significant 24 bits of the
    ///     modulus, which are its third-to-last and second-to-last octets.
    /// </summary>
    private static ushort ComputeRsaMd5SpecialCase(byte[] dnsKeyRdata)
    {
        // Flags (2) + Protocol (1) + Algorithm (1) precede the RFC 3110 public key field.
        byte[] publicKey = dnsKeyRdata[4..];

        int exponentLength;
        int offset;

        // RFC 3110 §2: the exponent length is one octet, or a zero octet followed by a 16-bit
        // length for longer exponents.
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
