using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Recursive.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Validation;

/// <summary>
///     This interface validates an <see cref="IterativeResolutionResult" /> against the DNSSEC
///     chain of trust (RFC 4035 §5). <see cref="RecursiveQueryResolver" /> and the forwarding
///     resolver call it; <see cref="DnssecValidator" /> implements it.
/// </summary>
public interface IDnssecValidator
{
    /// <summary>
    ///     This method validates one resolution's outcome.
    /// </summary>
    /// <param name="queriedName">The name the client queried.</param>
    /// <param name="queriedType">The record type the client queried.</param>
    /// <param name="recordClass">The query's class.</param>
    /// <param name="result">The resolution outcome to validate.</param>
    /// <param name="budget">
    ///     The budget the resolution used. Validation's DS and DNSKEY lookups count against it
    ///     too, so validation cannot issue unbounded extra queries.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <param name="fetcher">
    ///     Where the validator gets the DS and DNSKEY data of the chain of trust.
    ///     <see langword="null" /> (the default) uses the iterative engine; the forwarding
    ///     resolver passes its own so the chain comes from the same upstream servers.
    /// </param>
    /// <returns>The validation's outcome.</returns>
    Task<DnssecValidationResult> ValidateAsync(
        DnsName queriedName,
        DnsResourceRecordTypeEnum queriedType,
        DnsClassEnum recordClass,
        IterativeResolutionResult result,
        QueryBudget budget,
        CancellationToken stoppingToken = default,
        IDnssecRecordFetcher fetcher = null
    );
}
