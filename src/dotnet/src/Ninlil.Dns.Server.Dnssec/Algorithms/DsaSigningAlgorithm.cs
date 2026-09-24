using System.Buffers;
using System.Security.Cryptography;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Dnssec.Algorithms;

/// <summary>
///     This class implements the two DSA-based DNSSEC algorithms this project keeps for legacy
///     interop -- <see cref="DnssecAlgorithmEnum.Dsa" /> and <see cref="DnssecAlgorithmEnum.DsaNsec3Sha1" />
///     -- which RFC 5155 §2 gives an identical key and signature wire format to (the two
///     algorithm numbers exist only so an NSEC3-unaware validator can refuse to validate a zone
///     it would otherwise misjudge as insecure, not because the cryptography differs at all).
///     Both use RFC 2536's own idiosyncratic wire format, predating the cleaner RFC 3110-style
///     convention RSA uses: a one-octet "T" parameter derived from the key size, since RFC
///     2536 defines DSA for DNS only at 64-bit-aligned sizes between 512 and 1024 bits.
/// </summary>
/// <param name="algorithmNumber">Which of the two DSA-family algorithm numbers this instance is.</param>
public class DsaSigningAlgorithm(DnssecAlgorithmEnum algorithmNumber) : IDnssecSigningAlgorithm
{
    /// <summary>
    ///     This constant contains the DSA key size, in bits, this implementation always
    ///     generates -- the maximum RFC 2536 permits (<c>T = 8</c>), and the most broadly
    ///     interoperable classic DSA size.
    /// </summary>
    private const int KeySizeBits = 1024;

    /// <inheritdoc />
    public DnssecAlgorithmEnum AlgorithmNumber { get; } = algorithmNumber;

    /// <inheritdoc />
    public DnssecKeyPair GenerateKeyPair(int keySizeBits = 0)
    {
        using DSA dsa = DSA.Create(KeySizeBits);

        return new DnssecKeyPair(
            EncodePublicKey(dsa.ExportParameters(false)),
            dsa.ExportPkcs8PrivateKey()
        );
    }

    /// <inheritdoc />
    public byte[] Sign(byte[] signingInput, byte[] privateKeyPkcs8)
    {
        using DSA dsa = DSA.Create();
        dsa.ImportPkcs8PrivateKey(privateKeyPkcs8, out _);

        byte T = ComputeT(dsa.ExportParameters(false).P.Length);
        byte[] rAndS = dsa.SignData(
            signingInput,
            HashAlgorithmName.SHA1,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation
        );

        return [T, .. rAndS];
    }

    /// <inheritdoc />
    public bool Verify(byte[] signingInput, byte[] signature, byte[] publicKeyWireFormat)
    {
        using DSA dsa = DSA.Create();
        dsa.ImportParameters(DecodePublicKey(publicKeyWireFormat));

        // Byte 0 is the RFC 2536 "T" parameter, redundant with the public key's own P length and
        // not something VerifyData needs -- only the 40-octet R||S concatenation that follows it.
        return dsa.VerifyData(
            signingInput,
            signature[1..],
            HashAlgorithmName.SHA1,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation
        );
    }

    /// <summary>
    ///     This method encodes a DSA public key per RFC 2536 §2: T, then Q, then P, G, and Y --
    ///     each of the latter three exactly <c>64 + 8T</c> octets long, per the RFC's own
    ///     constraint on P's size.
    /// </summary>
    internal static byte[] EncodePublicKey(DSAParameters parameters)
    {
        ArrayBufferWriter<byte> buffer = new();

        buffer.GetSpan(1)[0] = ComputeT(parameters.P.Length);
        buffer.Advance(1);

        WriteBytes(buffer, parameters.Q);
        WriteBytes(buffer, parameters.P);
        WriteBytes(buffer, parameters.G);
        WriteBytes(buffer, parameters.Y);

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    ///     This method decodes a DSA public key per RFC 2536 §2, the reverse of
    ///     <see cref="EncodePublicKey" />.
    /// </summary>
    internal static DSAParameters DecodePublicKey(byte[] wireFormat)
    {
        int fieldLength = 64 + 8 * wireFormat[0];
        int offset = 1;

        byte[] q = wireFormat[offset..(offset += 20)];
        byte[] p = wireFormat[offset..(offset += fieldLength)];
        byte[] g = wireFormat[offset..(offset += fieldLength)];
        byte[] y = wireFormat[offset..(offset += fieldLength)];

        return new DSAParameters
        {
            Q = q,
            P = p,
            G = g,
            Y = y,
        };
    }

    /// <summary>
    ///     This method computes RFC 2536's "T" parameter from a DSA modulus's byte length, the
    ///     inverse of the RFC's own <c>length(P) = 64 + 8T</c> relationship.
    /// </summary>
    private static byte ComputeT(int modulusLength) => (byte)((modulusLength - 64) / 8);

    /// <summary>
    ///     This method appends a raw byte sequence to a buffer.
    /// </summary>
    private static void WriteBytes(IBufferWriter<byte> buffer, byte[] bytes)
    {
        bytes.CopyTo(buffer.GetSpan(bytes.Length));
        buffer.Advance(bytes.Length);
    }
}
