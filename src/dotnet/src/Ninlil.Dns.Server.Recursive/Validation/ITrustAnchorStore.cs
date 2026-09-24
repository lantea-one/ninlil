// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Validation;

/// <summary>
///     This interface holds this resolver's own current set of trust anchor candidates, in
///     every RFC 5011 state, persisting it durably enough that a pending key's hold-down clock
///     survives a process restart.
/// </summary>
public interface ITrustAnchorStore
{
    /// <summary>
    ///     This method returns every currently <see cref="TrustAnchorStateEnum.Valid" /> anchor,
    ///     in the smaller shape actual signature verification needs.
    /// </summary>
    IReadOnlyList<TrustAnchor> GetValidAnchors();

    /// <summary>
    ///     This method returns every tracked candidate, regardless of state -- used only by
    ///     <see cref="TrustAnchorRolloverService" /> to run RFC 5011's own state machine.
    /// </summary>
    IReadOnlyList<PersistedTrustAnchor> GetAll();

    /// <summary>
    ///     This method replaces this store's entire tracked set, both in memory and on whatever
    ///     durable storage backs it.
    /// </summary>
    /// <param name="anchors">The complete, new set of tracked candidates.</param>
    void Save(IReadOnlyList<PersistedTrustAnchor> anchors);
}
