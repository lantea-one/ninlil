using System.Security.Cryptography;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;

// Define our namespace.
namespace Ninlil.Dns.Server.Dnssec;

/// <summary>
///     This class computes a <see cref="DsRecordData.Digest" /> per RFC 4034 §5.1.4: a hash of
///     the keyed <see cref="DnsKeyRecordData" />'s owner name (in canonical form) concatenated
///     with its own RDATA, letting a parent zone vouch for a child's key without publishing the
///     full key itself.
/// </summary>
public static class DsDigestCodec
{
    /// <summary>
    ///     This method computes a DS digest.
    /// </summary>
    /// <param name="digestType">
    ///     Which digest algorithm to use, per IANA's "Delegation Signer (DS) Resource Record (RR)
    ///     Type Digest Algorithms" registry -- 1 (SHA-1), 2 (SHA-256), and 4 (SHA-384) are
    ///     supported; 3 (GOST R 34.11-94) is not, for the same reason algorithm 12 is excluded
    ///     from <see cref="Algorithms.DnssecAlgorithmRegistry" />.
    /// </param>
    /// <param name="ownerName">The keyed DNSKEY's own owner name.</param>
    /// <param name="dnsKeyRdata">
    ///     The keyed DNSKEY's complete RDATA, exactly as <see cref="DnsKeyRecordDataCodec" />
    ///     would write it.
    /// </param>
    /// <returns>The computed digest.</returns>
    /// <exception cref="NotSupportedException">
    ///     Thrown when <paramref name="digestType" /> is not one of the supported values above.
    /// </exception>
    public static byte[] Compute(byte digestType, DnsName ownerName, byte[] dnsKeyRdata)
    {
        byte[] ownerNameBytes = ownerName.ToCanonicalLower().ToUncompressedWireBytes();
        byte[] input = [.. ownerNameBytes, .. dnsKeyRdata];

        return digestType switch
        {
            1 => SHA1.HashData(input),
            2 => SHA256.HashData(input),
            4 => SHA384.HashData(input),
            _ => throw new NotSupportedException($"DS digest type {digestType} is not supported."),
        };
    }
}
