using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Dnssec.Algorithms;

/// <summary>
///     This class implements the RSA DNSSEC algorithms:
///     <see cref="DnssecAlgorithmEnum.RsaMd5" />, <see cref="DnssecAlgorithmEnum.RsaSha1" />,
///     <see cref="DnssecAlgorithmEnum.RsaSha1Nsec3Sha1" />, <see cref="DnssecAlgorithmEnum.RsaSha256" />
///     and <see cref="DnssecAlgorithmEnum.RsaSha512" />. They differ only in the hash; the public
///     key format (RFC 3110), private key handling and PKCS#1 v1.5 padding are the same.
/// </summary>
/// <param name="algorithmNumber">Which of the five RSA algorithm numbers this instance handles.</param>
/// <param name="hashAlgorithm">The hash algorithm this algorithm number signs with.</param>
public class RsaSigningAlgorithm(
    DnssecAlgorithmEnum algorithmNumber,
    HashAlgorithmName hashAlgorithm
) : IDnssecSigningAlgorithm
{
    /// <summary>
    ///     This constant contains the RSA key size, in bits, generated when a caller does not
    ///     request a specific size.
    /// </summary>
    private const int DefaultKeySizeBits = 2048;

    /// <summary>
    ///     This constant contains the smallest RSA key size, in bits, that a new key may have.
    ///     Keys that are already stored with a smaller modulus keep signing and verifying; only
    ///     generation enforces the floor.
    /// </summary>
    public const int MinimumKeySizeBits = 2048;

    /// <summary>
    ///     This constant contains the largest RSA key size, in bits, that a new key may have. The
    ///     RSA/SHA-256 and RSA/SHA-512 algorithms allow moduli up to 4096 bits (RFC 5702 §2.1,
    ///     §2.2).
    /// </summary>
    public const int MaximumKeySizeBits = 4096;

    /// <summary>
    ///     This constant contains the step, in bits, that a new key's size must be a multiple of.
    ///     Windows CNG only generates RSA keys in steps of 64 bits, while OpenSSL accepts any
    ///     size, so the rule keeps one answer on every platform.
    /// </summary>
    public const int KeySizeStepBits = 64;

    /// <summary>
    ///     This method tells whether <paramref name="keySizeBits" /> is a size a new RSA key may
    ///     have: 0 (the default size), or a multiple of <see cref="KeySizeStepBits" /> between
    ///     <see cref="MinimumKeySizeBits" /> and <see cref="MaximumKeySizeBits" />.
    /// </summary>
    public static bool IsValidKeySize(int keySizeBits) =>
        keySizeBits == 0
        || (
            keySizeBits is >= MinimumKeySizeBits and <= MaximumKeySizeBits
            && keySizeBits % KeySizeStepBits == 0
        );

    /// <inheritdoc />
    public DnssecAlgorithmEnum AlgorithmNumber { get; } = algorithmNumber;

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="keySizeBits" /> is not 0 and not a multiple of
    ///     <see cref="KeySizeStepBits" /> between <see cref="MinimumKeySizeBits" /> and
    ///     <see cref="MaximumKeySizeBits" />.
    /// </exception>
    public DnssecKeyPair GenerateKeyPair(int keySizeBits = 0)
    {
        if (!IsValidKeySize(keySizeBits))
            throw new ArgumentOutOfRangeException(
                nameof(keySizeBits),
                keySizeBits,
                $"RSA key size must be a multiple of {KeySizeStepBits} between {MinimumKeySizeBits} and {MaximumKeySizeBits} bits; 0 means {DefaultKeySizeBits}."
            );

        using RSA rsa = RSA.Create(keySizeBits > 0 ? keySizeBits : DefaultKeySizeBits);

        return new DnssecKeyPair(
            EncodePublicKey(rsa.ExportParameters(false)),
            rsa.ExportPkcs8PrivateKey()
        );
    }

    /// <inheritdoc />
    public byte[] Sign(byte[] signingInput, byte[] privateKeyPkcs8)
    {
        using RSA rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(privateKeyPkcs8, out _);

        return rsa.SignData(signingInput, hashAlgorithm, RSASignaturePadding.Pkcs1);
    }

    /// <inheritdoc />
    public bool Verify(byte[] signingInput, byte[] signature, byte[] publicKeyWireFormat)
    {
        using RSA rsa = RSA.Create();
        rsa.ImportParameters(DecodePublicKey(publicKeyWireFormat));

        return rsa.VerifyData(signingInput, signature, hashAlgorithm, RSASignaturePadding.Pkcs1);
    }

    /// <summary>
    ///     This method encodes an RSA public key per RFC 3110: the exponent length (one octet, or
    ///     a zero octet and a two-octet length for exponents over 255 octets), the exponent, then
    ///     the modulus. Leading zero octets are stripped from both, as RFC 3110 requires.
    /// </summary>
    internal static byte[] EncodePublicKey(RSAParameters parameters)
    {
        byte[] exponent = StripLeadingZeros(parameters.Exponent);
        byte[] modulus = StripLeadingZeros(parameters.Modulus);
        ArrayBufferWriter<byte> buffer = new();

        if (exponent.Length <= 255)
        {
            buffer.GetSpan(1)[0] = (byte)exponent.Length;
            buffer.Advance(1);
        }
        else
        {
            Span<byte> extendedLength = buffer.GetSpan(3);
            extendedLength[0] = 0;
            BinaryPrimitives.WriteUInt16BigEndian(extendedLength[1..], (ushort)exponent.Length);
            buffer.Advance(3);
        }

        exponent.CopyTo(buffer.GetSpan(exponent.Length));
        buffer.Advance(exponent.Length);

        modulus.CopyTo(buffer.GetSpan(modulus.Length));
        buffer.Advance(modulus.Length);

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    ///     This method decodes an RSA public key per RFC 3110, the reverse of
    ///     <see cref="EncodePublicKey" />.
    /// </summary>
    internal static RSAParameters DecodePublicKey(byte[] wireFormat)
    {
        int exponentLength;
        int offset;

        if (wireFormat[0] == 0)
        {
            exponentLength = BinaryPrimitives.ReadUInt16BigEndian(wireFormat.AsSpan(1, 2));
            offset = 3;
        }
        else
        {
            exponentLength = wireFormat[0];
            offset = 1;
        }

        byte[] exponent = wireFormat[offset..(offset + exponentLength)];
        byte[] modulus = wireFormat[(offset + exponentLength)..];

        return new RSAParameters { Exponent = exponent, Modulus = modulus };
    }

    /// <summary>
    ///     This method strips leading zero octets from a big-endian integer and always returns a
    ///     copy. A value of all zeros keeps one zero octet.
    /// </summary>
    private static byte[] StripLeadingZeros(byte[] value)
    {
        int start = 0;

        while (start < value.Length - 1 && value[start] == 0)
            start++;

        return value[start..];
    }
}
