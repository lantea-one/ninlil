using System.Security.Cryptography;

// Define our namespace.
namespace Ninlil.Dns.Server.DnsCrypt;

/// <summary>
///     This class draws the <c>&lt;client-magic&gt;</c> tag of a new certificate. DNSCrypt v2
///     section 12 gives two rules: no two valid certificates share a client magic, and a client
///     magic must not start with seven all-zero bytes, so a DNSCrypt query is never mistaken for
///     a QUIC packet. <see cref="DnsCryptCertificateRotationService" /> calls it for every
///     certificate it issues.
/// </summary>
public static class DnsCryptClientMagic
{
    /// <summary>
    ///     This constant contains how many leading zero bytes a client magic must not have.
    /// </summary>
    private const int ForbiddenZeroPrefixLength = 7;

    /// <summary>
    ///     This method reports whether <paramref name="clientMagic" /> is 8 bytes long and does not
    ///     start with seven zero bytes.
    /// </summary>
    public static bool IsAllowed(ReadOnlySpan<byte> clientMagic)
    {
        if (clientMagic.Length != DnsCryptEnvelopeCodec.ClientMagicLength)
            return false;

        return clientMagic[..ForbiddenZeroPrefixLength].ContainsAnyExcept((byte)0);
    }

    /// <summary>
    ///     This method draws 8 random bytes until they are an allowed client magic that no
    ///     certificate in <paramref name="inUse" /> carries.
    /// </summary>
    /// <param name="inUse">The client magic of every certificate that is still valid.</param>
    /// <param name="draw">
    ///     Returns a candidate; <see langword="null" /> draws from the cryptographic random source.
    ///     Tests pass a scripted source.
    /// </param>
    public static byte[] Generate(IEnumerable<byte[]> inUse, Func<byte[]> draw = null)
    {
        draw ??= () => RandomNumberGenerator.GetBytes(DnsCryptEnvelopeCodec.ClientMagicLength);
        List<byte[]> taken = [.. inUse];

        while (true)
        {
            byte[] candidate = draw();

            if (
                IsAllowed(candidate) && !taken.Any(other => other.AsSpan().SequenceEqual(candidate))
            )
                return candidate;
        }
    }
}
