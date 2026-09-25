using System.Buffers;
using System.Security.Cryptography;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Dnssec.Algorithms;

/// <summary>
///     This class implements the DSA DNSSEC algorithms, kept for interoperability with old zones:
///     <see cref="DnssecAlgorithmEnum.Dsa" /> and <see cref="DnssecAlgorithmEnum.DsaNsec3Sha1" />.
///     The cryptography is the same for both. RFC 5155 §2 added the second number so a validator
///     that does not understand NSEC3 treats the zone as unsigned instead of failing on its
///     denials. Both use the RFC 2536 wire format, which starts with a one-octet "T" parameter
///     derived from the key size, because RFC 2536 allows only sizes from 512 to 1024 bits in
///     64-bit steps. Signatures use SHA-1.
/// </summary>
/// <param name="algorithmNumber">Which of the two DSA algorithm numbers this instance handles.</param>
public class DsaSigningAlgorithm(DnssecAlgorithmEnum algorithmNumber) : IDnssecSigningAlgorithm
{
    /// <summary>
    ///     This constant contains the DSA key size, in bits, that is always generated: the
    ///     largest RFC 2536 allows (<c>T = 8</c>).
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

        // Octet 0 is the RFC 2536 "T" parameter; VerifyData needs only the 40-octet R||S after it.
        return dsa.VerifyData(
            signingInput,
            signature[1..],
            HashAlgorithmName.SHA1,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation
        );
    }

    /// <summary>
    ///     This method encodes a DSA public key per RFC 2536 §2: T, then Q (20 octets), then P, G
    ///     and Y, each <c>64 + 8T</c> octets long.
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
    ///     This method computes the RFC 2536 "T" parameter from the length of P in octets, by
    ///     inverting <c>length(P) = 64 + 8T</c>.
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
