using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Validation;

/// <summary>
///     This class binds one entry of <see cref="RecursiveServerOptions.TrustAnchorsOverride" />,
///     which replaces the built-in root trust anchors (<see cref="RootTrustAnchors" />) for a
///     deployment that validates against a private root or a test double.
/// </summary>
public sealed class TrustAnchorOption
{
    /// <summary>
    ///     This property contains the trusted <see cref="Common.Wire.Records.DnsKeyRecordData" />'s
    ///     key tag.
    /// </summary>
    public ushort KeyTag { get; set; }

    /// <summary>
    ///     This property contains the trusted key's algorithm.
    /// </summary>
    public DnssecAlgorithmEnum Algorithm { get; set; }

    /// <summary>
    ///     This property contains which digest algorithm <see cref="DigestHex" /> uses, per
    ///     IANA's "Delegation Signer (DS) Resource Record (RR) Type Digest Algorithms" registry.
    /// </summary>
    public byte DigestType { get; set; }

    /// <summary>
    ///     This property contains the trusted key's digest, as a hexadecimal string (parsed
    ///     via <see cref="Convert.FromHexString(string)" />).
    /// </summary>
    public string DigestHex { get; set; }
}
