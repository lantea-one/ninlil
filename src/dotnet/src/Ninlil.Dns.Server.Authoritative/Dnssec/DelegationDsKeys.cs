using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Server.Dnssec.Algorithms;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Dnssec;

/// <summary>
///     This class holds the one rule that says which keys of a child zone get a DS record in a
///     parent zone hosted on the same server. <see cref="ZoneSigningService" /> generates the DS
///     records from it, and <see cref="SignedZoneCache" /> compares its result before and after a
///     reload to know when the parent must be re-signed.
/// </summary>
internal static class DelegationDsKeys
{
    /// <summary>
    ///     This method reports whether a zone is a source of DS records for its parent: it is
    ///     marked DNSSEC-signed. The generator does not look at the zone's type or at
    ///     <c>IsDisabled</c>, so neither does this rule.
    /// </summary>
    /// <param name="zone">The zone found at the delegation name, or <see langword="null" />.</param>
    /// <returns>Whether the zone's keys give the parent DS records.</returns>
    internal static bool ContributesDs(ZoneEntity zone) => zone is { IsDnssecSigned: true };

    /// <summary>
    ///     This method reports whether a key gets a DS record in the parent: an Active KSK with a
    ///     permitted signing algorithm. In a Double-Signature KSK rollover (RFC 6781 §4.1.2) the
    ///     parent swaps the DS at the "DS change" stage rather than publishing both, so a KSK in
    ///     DoubleSignature gets none. A ZSK never gets one. A key with a prohibited algorithm is
    ///     not published (RFC 4035 §2.2 needs every DNSKEY algorithm to sign), so a DS for it
    ///     would match nothing.
    /// </summary>
    /// <param name="key">The key to test.</param>
    /// <returns>Whether the parent publishes a DS for the key.</returns>
    internal static bool GetsDs(DnssecKeyEntity key) =>
        key.LifecycleState == DnssecKeyLifecycleStateEnum.Active
        && key.Role == DnssecKeyRoleEnum.Ksk
        && !DnssecAlgorithmRegistry.IsSigningProhibited(key.Algorithm);

    /// <summary>
    ///     This method builds a stable text for the set of DS-relevant keys of a zone: the sorted
    ///     key tag, algorithm and public key of each. Two calls return the same text exactly when
    ///     the parent would generate the same DS records.
    /// </summary>
    /// <param name="keys">All keys of the zone.</param>
    /// <returns>The fingerprint; empty when no key gets a DS.</returns>
    internal static string Fingerprint(IEnumerable<DnssecKeyEntity> keys) =>
        string.Join(
            ';',
            keys.Where(GetsDs)
                .Select(key =>
                    $"{key.KeyTag}/{(int)key.Algorithm}/{Convert.ToBase64String(key.PublicKeyWireFormat)}"
                )
                .Order(StringComparer.Ordinal)
        );
}
