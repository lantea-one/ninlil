using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Recursive.Resolution;

namespace Ninlil.Dns.Server.Recursive.Tests.Fakes;

/// <summary>
///     This class is a fake IterativeResolutionEngine that returns canned results per name and
///     type, so DnssecValidator and RecursiveQueryResolver can be tested without resolution.
/// </summary>
public sealed class FakeIterativeResolutionEngine : IIterativeResolutionEngine
{
    private readonly Dictionary<
        (DnsName Name, DnsResourceRecordTypeEnum Type),
        IterativeResolutionResult
    > _results = [];

    public void AddResult(
        DnsName name,
        DnsResourceRecordTypeEnum type,
        IterativeResolutionResult result
    ) => _results[(name, type)] = result;

    public Task<IterativeResolutionResult> ResolveAsync(
        DnsName name,
        DnsResourceRecordTypeEnum type,
        DnsClassEnum recordClass,
        bool requestDnssec,
        DnsEdnsOption clientSubnetOption = null,
        CancellationToken stoppingToken = default
    ) => Resolve(name, type);

    public Task<IterativeResolutionResult> ResolveAsync(
        DnsName name,
        DnsResourceRecordTypeEnum type,
        DnsClassEnum recordClass,
        bool requestDnssec,
        QueryBudget budget,
        bool forceRootStart,
        DnsEdnsOption clientSubnetOption = null,
        CancellationToken stoppingToken = default
    ) => Resolve(name, type);

    public Task<IterativeResolutionResult> PrefetchAsync(
        DnsName name,
        DnsResourceRecordTypeEnum type,
        DnsClassEnum recordClass,
        bool requestDnssec,
        CancellationToken stoppingToken = default
    ) => Resolve(name, type);

    private Task<IterativeResolutionResult> Resolve(DnsName name, DnsResourceRecordTypeEnum type) =>
        Task.FromResult(
            _results.TryGetValue((name, type), out IterativeResolutionResult result)
                ? result
                : IterativeResolutionResult.ServFail()
        );
}
