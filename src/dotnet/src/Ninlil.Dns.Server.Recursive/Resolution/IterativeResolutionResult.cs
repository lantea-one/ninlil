using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Resolution;

/// <summary>
///     This class carries a single iterative resolution's complete outcome.
/// </summary>
/// <param name="Status">How resolution concluded.</param>
/// <param name="Answer">
///     The final answer, in order -- every CNAME hop followed, then the real records, for
///     <see cref="RecursiveResolutionStatusEnum.Success" />; empty otherwise.
/// </param>
/// <param name="DnssecRecords">
///     Whatever RRSIG/NSEC/NSEC3/DNSKEY content accompanied <see cref="Answer" /> or
///     <see cref="Soa" />, if DNSSEC data was requested and received at all.
/// </param>
/// <param name="Soa">
///     For <see cref="RecursiveResolutionStatusEnum.NxDomain" />/<see cref="RecursiveResolutionStatusEnum.NoData" />,
///     the authoritative zone's own SOA record. <see langword="null" /> otherwise.
/// </param>
/// <param name="ZoneCutsTraversed">
///     Every zone cut this resolution walked through, root first -- <see cref="Validation.DnssecValidator" />
///     walks this exact same chain to verify DS/DNSKEY at each step.
/// </param>
/// <param name="CnameChain">
///     Every CNAME hop this resolution followed to reach <see cref="Answer" />, in order, each
///     carrying its own owning zone and signature material -- see <see cref="Resolution.CnameChainHop" />.
///     Empty when no CNAME was followed. <see cref="Validation.DnssecValidator" /> walks this list
///     to authenticate each hop independently, rather than only the chain's terminal RRset.
/// </param>
public sealed record IterativeResolutionResult(
    RecursiveResolutionStatusEnum Status,
    IReadOnlyList<DnsResourceRecord> Answer,
    IReadOnlyList<DnsResourceRecord> DnssecRecords,
    DnsResourceRecord Soa,
    IReadOnlyList<DnsName> ZoneCutsTraversed,
    IReadOnlyList<CnameChainHop> CnameChain = null
)
{
    /// <summary>
    ///     Normalizes a <see langword="null" /> constructor argument (every pre-existing call site
    ///     that predates this property, none of which chased a CNAME chain of its own) down to an
    ///     empty list, so every caller of this record can always enumerate this property safely.
    /// </summary>
    public IReadOnlyList<CnameChainHop> CnameChain { get; init; } = CnameChain ?? [];

    /// <summary>
    ///     This method builds a <see cref="RecursiveResolutionStatusEnum.ServFail" /> result.
    /// </summary>
    public static IterativeResolutionResult ServFail() =>
        new(RecursiveResolutionStatusEnum.ServFail, [], [], null, []);
}
