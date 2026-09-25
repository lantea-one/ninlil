using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Recursive.Resolution;
using Ninlil.Dns.Server.Recursive.Validation;

namespace Ninlil.Dns.Server.Recursive.Tests.Fakes;

/// <summary>
///     This class is an IDnssecValidator that returns the same result for every call, for tests
///     that need a fixed validation outcome without a DS/DNSKEY chain.
/// </summary>
/// <param name="result">The fixed result every call returns.</param>
public sealed class StaticDnssecValidator(DnssecValidationResult result) : IDnssecValidator
{
    public Task<DnssecValidationResult> ValidateAsync(
        DnsName queriedName,
        DnsResourceRecordTypeEnum queriedType,
        DnsClassEnum recordClass,
        IterativeResolutionResult iterativeResult,
        QueryBudget budget,
        CancellationToken stoppingToken = default
    ) => Task.FromResult(result);
}
