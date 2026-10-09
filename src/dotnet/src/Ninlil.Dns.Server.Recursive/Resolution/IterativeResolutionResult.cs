using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Resolution;

/// <summary>
///     This record carries the outcome of one iterative resolution, as
///     <see cref="IIterativeResolutionEngine" /> returns it and <see cref="Validation.IDnssecValidator" />
///     checks it.
/// </summary>
/// <param name="Status">How resolution concluded.</param>
/// <param name="Answer">
///     The final answer in order, every CNAME hop followed and then the records of the queried
///     type, for <see cref="RecursiveResolutionStatusEnum.Success" />; empty otherwise.
/// </param>
/// <param name="DnssecRecords">
///     The RRSIG, NSEC, NSEC3 or DNSKEY records that came with the final RRset of
///     <see cref="Answer" /> or with <see cref="Soa" />, if DNSSEC data was requested. The CNAME
///     hops' signatures are in <see cref="CnameChain" />.
/// </param>
/// <param name="Soa">
///     For <see cref="RecursiveResolutionStatusEnum.NxDomain" />/<see cref="RecursiveResolutionStatusEnum.NoData" />,
///     the zone's SOA record. <see langword="null" /> otherwise.
/// </param>
/// <param name="ZoneCutsTraversed">
///     Every zone cut this resolution walked through, root first. For an answer without
///     signatures, <see cref="Validation.DnssecValidator" /> starts its walk for a delegation that
///     proves the answer unsigned at the last one; signed answers name their zone themselves. A
///     result built from a forwarder's answer has none, and the walk then starts at the root.
/// </param>
/// <param name="CnameChain">
///     Every CNAME hop followed to reach <see cref="Answer" />, in order, each with its zone and
///     signatures (see <see cref="Resolution.CnameChainHop" />). Empty when no CNAME was followed.
///     <see cref="Validation.DnssecValidator" /> checks each hop, not only the final RRset.
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
    ///     This property turns a <see langword="null" /> <c>CnameChain</c> argument into an empty
    ///     list, so callers can always enumerate it.
    /// </summary>
    public IReadOnlyList<CnameChainHop> CnameChain { get; init; } = CnameChain ?? [];

    /// <summary>
    ///     This method builds a <see cref="RecursiveResolutionStatusEnum.ServFail" /> result.
    /// </summary>
    public static IterativeResolutionResult ServFail() =>
        new(RecursiveResolutionStatusEnum.ServFail, [], [], null, []);
}
