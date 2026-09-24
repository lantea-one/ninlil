using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Resolution;

/// <summary>
///     This record carries one CNAME hop's own owner zone and accompanying DNSSEC material, as
///     captured live by <see cref="IterativeResolutionEngine" /> while it follows a CNAME chain.
///     <see cref="Validation.DnssecValidator" /> walks a result's own list of these to authenticate
///     each hop independently against whichever zone actually owns and signed it, per RFC 4035
///     §5.3 -- a signed parent can delegate, via CNAME, into a differently-signed (or entirely
///     unsigned) descendant zone, so each hop's own RRset can only ever be trusted against the
///     DNSKEY of the zone that actually produced it, never against the terminal answer's own zone.
/// </summary>
/// <param name="CnameRecord">The single CNAME record this hop followed.</param>
/// <param name="DnssecRecords">
///     Whatever RRSIG accompanied this hop's own CNAME RRset -- empty when DNSSEC data was not
///     requested/received for this hop, or when the zone that produced it is genuinely unsigned.
/// </param>
/// <param name="OwningZone">
///     The zone cut that actually served this hop's own CNAME RRset -- the zone
///     <see cref="Validation.DnssecValidator" /> must build a fresh chain of trust to before this
///     hop's own signature can be checked against it.
/// </param>
public sealed record CnameChainHop(
    DnsResourceRecord CnameRecord,
    IReadOnlyList<DnsResourceRecord> DnssecRecords,
    DnsName OwningZone
);
