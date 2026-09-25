using Ninlil.Dns.Server.Recursive.Validation;

namespace Ninlil.Dns.Server.Recursive.Tests.Fakes;

/// <summary>
///     This class is an in-memory trust anchor store with a fixed set of anchors, so a test
///     controls which root key DnssecValidator trusts without touching disk.
/// </summary>
public sealed class FakeTrustAnchorStore(IReadOnlyList<TrustAnchor> anchors) : ITrustAnchorStore
{
    public IReadOnlyList<TrustAnchor> GetValidAnchors() => anchors;

    public IReadOnlyList<PersistedTrustAnchor> GetAll() => [];

    public void Save(IReadOnlyList<PersistedTrustAnchor> anchors) { }
}
