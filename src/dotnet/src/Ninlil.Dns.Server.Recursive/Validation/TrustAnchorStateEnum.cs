// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Validation;

/// <summary>
///     This enumeration lists the states a single trust anchor candidate can be in, implementing
///     RFC 5011 §4.2's full five-state automated rollover model end to end.
///     <see cref="TrustAnchorRolloverService" /> drives every transition below.
/// </summary>
public enum TrustAnchorStateEnum
{
    /// <summary>
    ///     A new candidate key, observed in a validly-signed DNSKEY RRset but not yet trusted --
    ///     it must be seen continuously, on every check, for the configured add hold-down
    ///     duration before being promoted to <see cref="Valid" />.
    /// </summary>
    AddPend,

    /// <summary>
    ///     A key this resolver actively trusts and uses as a chain-of-trust starting point.
    /// </summary>
    Valid,

    /// <summary>
    ///     A previously-<see cref="Valid" /> key that this resolver's most recent check did not
    ///     observe in the target zone's own DNSKEY RRset at all (and which did not announce its
    ///     own revocation) -- RFC 5011 §4.2's deliberately conservative middle ground between
    ///     immediately distrusting a key on a single missed check and never noticing a real,
    ///     permanent retirement at all. A key reappearing while <see cref="Missing" /> returns
    ///     directly to <see cref="Valid" />; one that stays absent through the configured remove
    ///     hold-down duration transitions to <see cref="Removed" />.
    /// </summary>
    Missing,

    /// <summary>
    ///     A key that announced its own revocation (RFC 5011 §5.5) -- no longer trusted, kept in
    ///     the persisted store only so it is never mistakenly re-added as a fresh candidate.
    /// </summary>
    Revoked,

    /// <summary>
    ///     A key that stayed <see cref="Missing" /> for the full configured remove hold-down
    ///     duration without ever reappearing -- RFC 5011 §4.2's terminal state for a key that has
    ///     genuinely been retired rather than merely delayed by a transient issue. No longer
    ///     trusted; a key that later reappears from this state is treated as a brand-new
    ///     candidate, restarting the add hold-down clock from scratch, rather than silently
    ///     resurrected.
    /// </summary>
    Removed,
}
