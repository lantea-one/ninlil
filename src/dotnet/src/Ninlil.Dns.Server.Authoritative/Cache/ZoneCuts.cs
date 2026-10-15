using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Cache;

/// <summary>
///     This class holds the one rule that decides which names are zone cuts. <see cref="CachedZone" />
///     (referrals) and <c>ZoneSigningService</c> (what is signed and chained) both call it, so the
///     resolver and the signer cannot disagree about where this zone's authority ends.
/// </summary>
internal static class ZoneCuts
{
    /// <summary>
    ///     This method picks the topmost zone cuts from the names that own NS records: a non-apex
    ///     NS owner with no NS-owning ancestor below the apex. A cut nested inside another is
    ///     occluded data and is not returned (RFC 1034 §4.2.1; RFC 4035 §2.2). The apex NS records
    ///     name this zone's own servers and never make a cut.
    /// </summary>
    /// <param name="apexName">The zone's apex name.</param>
    /// <param name="nsOwners">Every owner name that has NS records, the apex included or not.</param>
    /// <returns>The topmost cut names.</returns>
    public static HashSet<DnsName> FindTopmostCuts(DnsName apexName, IEnumerable<DnsName> nsOwners)
    {
        HashSet<DnsName> candidates = [.. nsOwners.Where(name => name != apexName)];

        return
        [
            .. candidates.Where(name =>
                !Enumerable
                    .Range(apexName.Labels.Count + 1, name.Labels.Count - apexName.Labels.Count - 1)
                    .Any(depth =>
                        candidates.Contains(
                            new DnsName([.. name.Labels.Skip(name.Labels.Count - depth)])
                        )
                    )
            ),
        ];
    }

    /// <summary>
    ///     This method checks whether a name lies strictly below one of the cuts, which makes it
    ///     occluded: the child zone owns it, so this zone neither signs nor chains it.
    /// </summary>
    /// <param name="name">The name to check.</param>
    /// <param name="apexName">The zone's apex name.</param>
    /// <param name="cuts">The topmost cuts from <see cref="FindTopmostCuts" />.</param>
    /// <returns>Whether a cut is a proper ancestor of <paramref name="name" />.</returns>
    public static bool IsBelowACut(DnsName name, DnsName apexName, HashSet<DnsName> cuts) =>
        Enumerable
            .Range(
                apexName.Labels.Count + 1,
                Math.Max(0, name.Labels.Count - apexName.Labels.Count - 1)
            )
            .Any(depth =>
                cuts.Contains(new DnsName([.. name.Labels.Skip(name.Labels.Count - depth)]))
            );
}
