using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Recursive.Caching;

namespace Ninlil.Dns.Server.Recursive.Tests.Fakes;

/// <summary>
///     This class always reports a cache miss -- enough for <see cref="Validation.DnssecValidator" />
///     tests that never exercise its cache-hit-provenance fallback (<c>FindOwningZone</c>),
///     since every test result already carries a non-empty <c>ZoneCutsTraversed</c>.
/// </summary>
public sealed class EmptyRecursiveCache : IRecursiveCache
{
    public RecursiveCacheEntry TryGet(
        DnsName name,
        DnsResourceRecordTypeEnum type,
        DnsClassEnum recordClass
    ) => null;

    public RecursiveCacheEntry TryGetStale(
        DnsName name,
        DnsResourceRecordTypeEnum type,
        DnsClassEnum recordClass,
        TimeSpan maxStaleAge
    ) => null;

    public void StorePositive(
        DnsName name,
        DnsResourceRecordTypeEnum type,
        DnsClassEnum recordClass,
        IReadOnlyList<DnsResourceRecord> records,
        IReadOnlyList<DnsResourceRecord> dnssecRecords
    ) { }

    public void StoreNegative(
        DnsName name,
        DnsResourceRecordTypeEnum type,
        DnsClassEnum recordClass,
        DnsResponseCodeEnum responseCode,
        DnsResourceRecord soa,
        IReadOnlyList<DnsResourceRecord> dnssecRecords
    ) { }

    public int EvictExpiredAndOverCapacity() => 0;

    public IReadOnlyList<(
        DnsName Name,
        DnsResourceRecordTypeEnum Type,
        DnsClassEnum Class
    )> GetPrefetchCandidates(TimeSpan prefetchWindow, TimeSpan recentAccessWindow) => [];
}
