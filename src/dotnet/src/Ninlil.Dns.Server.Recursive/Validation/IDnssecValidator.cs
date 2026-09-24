using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Recursive.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Validation;

/// <summary>
///     This interface validates an <see cref="IterativeResolutionResult" /> against the DNSSEC
///     chain of trust, per RFC 4035 §5.
/// </summary>
public interface IDnssecValidator
{
    /// <summary>
    ///     This method validates a single resolution's outcome.
    /// </summary>
    /// <param name="queriedName">The name the client actually queried.</param>
    /// <param name="queriedType">The record type the client actually queried.</param>
    /// <param name="recordClass">The query's class.</param>
    /// <param name="result">The resolution outcome to validate.</param>
    /// <param name="budget">
    ///     The same budget the original resolution was bounded by -- every additional lookup
    ///     this validation needs (DS and DNSKEY at each zone cut) counts against it too, so a
    ///     validating resolution cannot be made to issue unbounded extra upstream queries beyond
    ///     what already bounds a plain resolution.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The validation's outcome.</returns>
    Task<DnssecValidationResult> ValidateAsync(
        DnsName queriedName,
        DnsResourceRecordTypeEnum queriedType,
        DnsClassEnum recordClass,
        IterativeResolutionResult result,
        QueryBudget budget,
        CancellationToken stoppingToken = default
    );
}
