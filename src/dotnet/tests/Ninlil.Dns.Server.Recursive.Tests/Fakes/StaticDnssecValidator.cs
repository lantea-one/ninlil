using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Recursive.Resolution;
using Ninlil.Dns.Server.Recursive.Validation;

namespace Ninlil.Dns.Server.Recursive.Tests.Fakes;

/// <summary>
///     This class stands in for a real <see cref="IDnssecValidator" />, always returning the same,
///     caller-supplied <see cref="DnssecValidationResult" /> regardless of what it is actually
///     asked to validate -- useful for a test that needs a fixed validation outcome (most commonly
///     <see cref="DnssecValidationResult.Secure" />) without needing a real DS/DNSKEY chain
///     underneath it.
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
