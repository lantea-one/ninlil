// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Validation;

/// <summary>
///     This interface holds the resolver's trust anchor candidates in every RFC 5011 state, and
///     persists them so a pending key's hold-down clock survives a restart.
///     <see cref="DnssecValidator" /> reads the valid anchors; <see cref="TrustAnchorRolloverService" />
///     updates the set. <see cref="FileTrustAnchorStore" /> implements it.
/// </summary>
public interface ITrustAnchorStore
{
    /// <summary>
    ///     This method returns every <see cref="TrustAnchorStateEnum.Valid" /> and
    ///     <see cref="TrustAnchorStateEnum.Missing" /> anchor, in the smaller shape validation
    ///     needs. A Missing key stays a trust point (RFC 5011 §4), so a check that fails to find
    ///     the only root KSK does not switch validation off.
    ///     was included.
    /// </summary>
    IReadOnlyList<TrustAnchor> GetValidAnchors();

    /// <summary>
    ///     This method returns every tracked candidate in any state, for
    ///     <see cref="TrustAnchorRolloverService" /> to run the RFC 5011 state machine on.
    /// </summary>
    IReadOnlyList<PersistedTrustAnchor> GetAll();

    /// <summary>
    ///     This method replaces the whole tracked set, in memory and in durable storage.
    /// </summary>
    /// <param name="anchors">The complete, new set of tracked candidates.</param>
    void Save(IReadOnlyList<PersistedTrustAnchor> anchors);
}
