using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Recursive.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Validation;

/// <summary>
///     This interface is where <see cref="DnssecValidator" /> gets the DS and DNSKEY RRsets (and
///     the NSEC or NSEC3 proofs of a missing DS) it builds a chain of trust from. The iterative
///     engine supplies them for recursion (<see cref="IterativeDnssecRecordFetcher" />); the
///     forwarding resolver can supply its own, so a network that only reaches its forwarders still
///     gets a chain. Every fetch must request DNSSEC records and must count against the caller's
///     <see cref="QueryBudget" />.
/// </summary>
public interface IDnssecRecordFetcher
{
    /// <summary>
    ///     This method fetches one RRset with its DNSSEC records.
    /// </summary>
    /// <param name="name">The owner name to fetch.</param>
    /// <param name="type">The record type, normally DS or DNSKEY.</param>
    /// <param name="recordClass">The query's class.</param>
    /// <param name="budget">The budget of the client query the validation belongs to.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     The outcome in the engine's result shape: <see cref="RecursiveResolutionStatusEnum.Success" />
    ///     with the records, NODATA or NXDOMAIN with the proof, or any other status when nothing
    ///     usable was obtained.
    /// </returns>
    Task<IterativeResolutionResult> FetchAsync(
        DnsName name,
        DnsResourceRecordTypeEnum type,
        DnsClassEnum recordClass,
        QueryBudget budget,
        CancellationToken stoppingToken
    );
}
