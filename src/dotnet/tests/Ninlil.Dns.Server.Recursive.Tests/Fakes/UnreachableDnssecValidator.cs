using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Recursive.Resolution;
using Ninlil.Dns.Server.Recursive.Validation;

namespace Ninlil.Dns.Server.Recursive.Tests.Fakes;

/// <summary>
///     This class is an IDnssecValidator that fails the test if called, for tests with
///     DNSSEC validation off, where RecursiveQueryResolver must not call a validator.
/// </summary>
public sealed class UnreachableDnssecValidator : IDnssecValidator
{
    public Task<DnssecValidationResult> ValidateAsync(
        DnsName queriedName,
        DnsResourceRecordTypeEnum queriedType,
        DnsClassEnum recordClass,
        IterativeResolutionResult result,
        QueryBudget budget,
        CancellationToken stoppingToken = default
    ) =>
        throw new InvalidOperationException(
            "This validator must never be called while DNSSEC validation is disabled."
        );
}
