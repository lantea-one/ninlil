using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Resolution;

/// <summary>
///     This record carries one CNAME hop, with its signatures and the zone that served it, as
///     <see cref="IterativeResolutionEngine" /> records it while following a chain.
///     <see cref="Validation.DnssecValidator" /> checks each hop against the zone that signed it
///     (RFC 4035 §5.3): a chain can cross into differently signed or unsigned zones, so a hop
///     can only be verified with its own zone's DNSKEY, not the final answer's.
/// </summary>
/// <param name="CnameRecord">The single CNAME record this hop followed.</param>
/// <param name="DnssecRecords">
///     The RRSIGs that came with this hop's CNAME RRset; empty when DNSSEC data was not requested
///     or the zone is unsigned.
/// </param>
/// <param name="OwningZone">
///     The zone that served this hop's CNAME RRset. <see cref="Validation.DnssecValidator" />
///     verifies a signed hop against the zone named by the RRSIG, and starts from this zone only
///     to decide whether an unsigned hop sits below an unsigned delegation.
/// </param>
public sealed record CnameChainHop(
    DnsResourceRecord CnameRecord,
    IReadOnlyList<DnsResourceRecord> DnssecRecords,
    DnsName OwningZone
);
