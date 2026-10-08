using System.Security.Cryptography;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Tsig;

/// <summary>
///     This class describes one TSIG MAC algorithm Ninlil implements and computes its HMAC. RFC 8945
///     §6 makes HMAC-SHA1 and HMAC-SHA256 mandatory to implement; HMAC-SHA384 and HMAC-SHA512 are
///     optional and supported too. <see cref="TsigService" /> looks the algorithm up by the name in
///     the TSIG record or key, and the TSIG key API in <c>Ninlil.Web</c> offers
///     <see cref="Supported" /> as the choice for a new key. HMAC-MD5 and the truncated variants are
///     not supported.
/// </summary>
public sealed class TsigAlgorithm
{
    /// <summary>
    ///     This property contains every supported algorithm, strongest last.
    /// </summary>
    public static IReadOnlyList<TsigAlgorithm> Supported { get; } =
    [
        new(DnsTsigOptions.HmacSha1AlgorithmName, 20, HMACSHA1.HashData),
        new(DnsTsigOptions.HmacSha256AlgorithmName, 32, HMACSHA256.HashData),
        new(DnsTsigOptions.HmacSha384AlgorithmName, 48, HMACSHA384.HashData),
        new(DnsTsigOptions.HmacSha512AlgorithmName, 64, HMACSHA512.HashData),
    ];

    /// <summary>
    ///     This property contains the algorithm name as written in a TSIG record
    ///     (for example <c>hmac-sha256</c>).
    /// </summary>
    public DnsName Name { get; }

    private readonly Func<byte[], byte[], byte[]> _hash;

    /// <summary>
    ///     This property contains the length, in octets, of the full (untruncated) MAC.
    /// </summary>
    public int DigestLength { get; }

    /// <summary>
    ///     This constructor is private; use <see cref="Find(DnsName)" /> or <see cref="Supported" />.
    /// </summary>
    private TsigAlgorithm(DnsName name, int digestLength, Func<byte[], byte[], byte[]> hash)
    {
        Name = name;
        DigestLength = digestLength;
        _hash = hash;
    }

    /// <summary>
    ///     This method finds a supported algorithm by name. Names compare case-insensitively and
    ///     with or without the trailing dot, because a stored key keeps the presentation form it
    ///     was created with.
    /// </summary>
    /// <param name="name">The algorithm name, or null.</param>
    /// <returns>The algorithm, or null when the name is empty, malformed or not supported.</returns>
    public static TsigAlgorithm Find(DnsName name) =>
        name is null ? null : Supported.FirstOrDefault(algorithm => algorithm.Name == name);

    /// <summary>
    ///     This method finds a supported algorithm by its presentation-form name, such as the
    ///     <c>hmac-sha256.</c> stored in a key.
    /// </summary>
    /// <param name="name">The algorithm name, or null.</param>
    /// <returns>The algorithm, or null when the name is empty, malformed or not supported.</returns>
    public static TsigAlgorithm Find(string name) =>
        !string.IsNullOrWhiteSpace(name) && DnsName.TryParse(name, out DnsName parsed)
            ? Find(parsed)
            : null;

    /// <summary>
    ///     This method computes the full-length HMAC of <paramref name="data" /> under
    ///     <paramref name="secret" />.
    /// </summary>
    /// <param name="secret">The shared secret.</param>
    /// <param name="data">The digest input from <see cref="TsigCodec.BuildDigestInput" />.</param>
    /// <returns>The MAC, <see cref="DigestLength" /> octets long.</returns>
    public byte[] ComputeMac(byte[] secret, byte[] data) => _hash(secret, data);
}
