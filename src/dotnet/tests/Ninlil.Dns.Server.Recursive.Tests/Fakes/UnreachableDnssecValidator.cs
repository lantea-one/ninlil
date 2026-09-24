using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Recursive.Resolution;
using Ninlil.Dns.Server.Recursive.Validation;

namespace Ninlil.Dns.Server.Recursive.Tests.Fakes;

/// <summary>
///     This class stands in for a real <see cref="IDnssecValidator" /> in a test that disables
///     <see cref="RecursiveServerOptions.EnableDnssecValidation" /> -- <see cref="RecursiveQueryResolver" />
///     must never call it in that configuration, so any call reaching this fake fails the test
///     loudly rather than silently returning a plausible-looking result.
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
