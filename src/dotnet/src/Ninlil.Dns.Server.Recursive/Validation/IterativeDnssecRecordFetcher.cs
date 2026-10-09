using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Recursive.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Validation;

/// <summary>
///     This class implements <see cref="IDnssecRecordFetcher" /> with the iterative engine.
///     <see cref="DnssecValidator" /> uses it when the caller passes no fetcher of its own. The
///     engine's cache answers repeated lookups, so a chain that is already known costs no upstream
///     query.
/// </summary>
/// <param name="engine">The engine that resolves the lookups.</param>
public sealed class IterativeDnssecRecordFetcher(IIterativeResolutionEngine engine)
    : IDnssecRecordFetcher
{
    /// <inheritdoc />
    public Task<IterativeResolutionResult> FetchAsync(
        DnsName name,
        DnsResourceRecordTypeEnum type,
        DnsClassEnum recordClass,
        QueryBudget budget,
        CancellationToken stoppingToken
    ) =>
        engine.ResolveAsync(
            name,
            type,
            recordClass,
            true,
            budget,
            forceRootStart: false,
            clientSubnetOption: null,
            stoppingToken: stoppingToken
        );
}
