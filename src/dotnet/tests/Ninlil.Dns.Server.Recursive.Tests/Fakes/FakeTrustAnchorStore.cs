using Ninlil.Dns.Server.Recursive.Validation;

namespace Ninlil.Dns.Server.Recursive.Tests.Fakes;

/// <summary>
///     This class hands back a fixed, caller-supplied set of trust anchors -- standing in for
///     <see cref="FileTrustAnchorStore" /> so a test can control exactly which root key
///     <see cref="DnssecValidator" /> is meant to trust, without touching disk.
/// </summary>
public sealed class FakeTrustAnchorStore(IReadOnlyList<TrustAnchor> anchors) : ITrustAnchorStore
{
    public IReadOnlyList<TrustAnchor> GetValidAnchors() => anchors;

    public IReadOnlyList<PersistedTrustAnchor> GetAll() => [];

    public void Save(IReadOnlyList<PersistedTrustAnchor> anchors) { }
}
