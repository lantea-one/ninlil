// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Validation;

/// <summary>
///     This enumeration lists the states a trust anchor candidate can be in, after the RFC 5011
///     rollover model. <see cref="TrustAnchorRolloverService" /> drives every transition, and
///     only <see cref="Valid" /> anchors are used for validation.
/// </summary>
public enum TrustAnchorStateEnum
{
    /// <summary>
    ///     A new candidate key seen in a signed DNSKEY RRset but not yet trusted. It must be seen
    ///     on every check for the add hold-down duration before it becomes <see cref="Valid" />;
    ///     missing one check drops it.
    /// </summary>
    AddPend,

    /// <summary>
    ///     A key the resolver trusts and uses as a chain-of-trust starting point.
    /// </summary>
    Valid,

    /// <summary>
    ///     A <see cref="Valid" /> key that the latest check did not find in the root DNSKEY RRset,
    ///     without a revocation. It is not used for validation meanwhile. If it reappears it
    ///     returns to <see cref="Valid" />; if it stays absent for the remove hold-down duration it
    ///     becomes <see cref="Removed" />.
    /// </summary>
    Missing,

    /// <summary>
    ///     A key that announced its revocation with the REVOKE bit (RFC 5011). It is no longer
    ///     trusted, and is kept in the store so it is never re-added as a new candidate.
    /// </summary>
    Revoked,

    /// <summary>
    ///     A key that stayed <see cref="Missing" /> for the whole remove hold-down duration. It is
    ///     no longer trusted; if it reappears it starts again as a new <see cref="AddPend" />
    ///     candidate.
    /// </summary>
    Removed,
}
