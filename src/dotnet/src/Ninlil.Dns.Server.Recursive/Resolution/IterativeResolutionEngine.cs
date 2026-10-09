using System.Net;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Recursive.Caching;
using Ninlil.Dns.Server.Recursive.Transport;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Resolution;

/// <summary>
///     This class implements <see cref="IIterativeResolutionEngine" /> with the RFC 1035 §7
///     algorithm: start from the closest zone cut with cached name servers (or the root), query
///     a server, and either accept its answer, follow its referral one cut deeper, or chase a
///     CNAME. Answers, negative answers, referrals and in-bailiwick glue are cached in
///     <see cref="IRecursiveCache" />.
///     <para>
///         Name server addresses are tried in order until one returns a usable response. A
///         SERVFAIL, REFUSED, NOTIMP or FORMERR to the full query says nothing about the name
///         (the server is broken or lame), so the next server is tried; when every server fails
///         this way the resolution fails, with serve-stale as the fallback. When
///         <see cref="RecursiveServerOptions.EnableQnameMinimization" /> is on, intermediate
///         servers get RFC 9156 minimized NS queries, with the fallbacks noted below. Every
///         query, and every step the cache answers, counts against the <see cref="QueryBudget" />.
///     </para>
/// </summary>
/// <param name="cache">The cache this engine reads name servers and answers from, and writes them back to.</param>
/// <param name="upstreamClient">The client used to send a query to a chosen name server.</param>
/// <param name="options">
///     The bounds (referral depth, CNAME chain length, query budget), QNAME minimization,
///     serve-stale and root hints override, read live through <see cref="IOptionsMonitor{TOptions}" />
///     so settings changes apply without a restart.
/// </param>
public sealed class IterativeResolutionEngine(
    IRecursiveCache cache,
    IUpstreamDnsClient upstreamClient,
    IOptionsMonitor<RecursiveServerOptions> options
) : IIterativeResolutionEngine
{
    /// <inheritdoc />
    public Task<IterativeResolutionResult> ResolveAsync(
        DnsName name,
        DnsResourceRecordTypeEnum type,
        DnsClassEnum recordClass,
        bool requestDnssec,
        DnsEdnsOption clientSubnetOption = null,
        CancellationToken stoppingToken = default
    )
    {
        QueryBudget budget = new(options.CurrentValue);

        return ResolveInternalAsync(
            name,
            type,
            recordClass,
            requestDnssec,
            budget,
            cnameDepth: 0,
            [],
            [],
            forceRootStart: false,
            clientSubnetOption,
            bypassCache: false,
            stoppingToken
        );
    }

    /// <inheritdoc />
    public Task<IterativeResolutionResult> ResolveAsync(
        DnsName name,
        DnsResourceRecordTypeEnum type,
        DnsClassEnum recordClass,
        bool requestDnssec,
        QueryBudget budget,
        bool forceRootStart,
        DnsEdnsOption clientSubnetOption = null,
        CancellationToken stoppingToken = default
    ) =>
        ResolveInternalAsync(
            name,
            type,
            recordClass,
            requestDnssec,
            budget,
            cnameDepth: 0,
            [],
            [],
            forceRootStart,
            clientSubnetOption,
            bypassCache: false,
            stoppingToken
        );

    /// <inheritdoc />
    public Task<IterativeResolutionResult> PrefetchAsync(
        DnsName name,
        DnsResourceRecordTypeEnum type,
        DnsClassEnum recordClass,
        bool requestDnssec,
        CancellationToken stoppingToken = default
    )
    {
        QueryBudget budget = new(options.CurrentValue);

        // bypassCache skips the fresh-cache shortcut for the queried name, since a prefetch
        // candidate is still fresh and would otherwise be returned unchanged.
        return ResolveInternalAsync(
            name,
            type,
            recordClass,
            requestDnssec,
            budget,
            cnameDepth: 0,
            [],
            [],
            forceRootStart: false,
            clientSubnetOption: null,
            bypassCache: true,
            stoppingToken
        );
    }

    /// <summary>
    ///     This method does the resolution, calling itself once per CNAME hop and for name server
    ///     addresses it has to look up.
    /// </summary>
    private async Task<IterativeResolutionResult> ResolveInternalAsync(
        DnsName name,
        DnsResourceRecordTypeEnum type,
        DnsClassEnum recordClass,
        bool requestDnssec,
        QueryBudget budget,
        int cnameDepth,
        IReadOnlyList<DnsResourceRecord> precedingChain,
        IReadOnlyList<CnameChainHop> precedingHops,
        bool forceRootStart,
        DnsEdnsOption clientSubnetOption,
        bool bypassCache,
        CancellationToken stoppingToken
    )
    {
        // Every step counts, including those the cache answers, since a cached delegation can
        // otherwise send this method round in circles without issuing a query.
        if (!budget.TryConsumeStep())
            return TryServeStale(name, type, recordClass, precedingChain, precedingHops, [])
                ?? IterativeResolutionResult.ServFail();

        // A cached answer (positive or negative) is returned directly. The cache does not record
        // which zone it came from, so BuildResultFromCacheEntry recovers the zone cut from the
        // cached NS sets, which DNSSEC validation needs. Prefetch skips this (see PrefetchAsync).
        RecursiveCacheEntry cachedExact = bypassCache
            ? null
            : cache.TryGet(name, type, recordClass);

        if (cachedExact is not null)
            return BuildResultFromCacheEntry(
                cachedExact,
                name,
                type,
                precedingChain,
                precedingHops
            );

        // A cached CNAME at this name means the alias is its only data, so chase it without
        // going to the network.
        if (type != DnsResourceRecordTypeEnum.Cname)
        {
            RecursiveCacheEntry cachedCname = cache.TryGet(
                name,
                DnsResourceRecordTypeEnum.Cname,
                recordClass
            );

            if (cachedCname is { IsNegative: false })
            {
                // Recover the CNAME's zone from the cached NS sets, so DnssecValidator knows where
                // an unsigned hop sits.
                DnsName owningZone = FindBestNameServers(
                    name,
                    DnsResourceRecordTypeEnum.Cname,
                    false
                ).ZoneCut;

                return await ChaseCnameAsync(
                    cachedCname.Records,
                    cachedCname.DnssecRecords,
                    owningZone,
                    type,
                    recordClass,
                    requestDnssec,
                    budget,
                    cnameDepth,
                    precedingChain,
                    precedingHops,
                    clientSubnetOption,
                    stoppingToken
                );
            }
        }

        (DnsName zoneCut, List<DnsName> nameServerNames, List<IPAddress> hintAddresses) =
            FindBestNameServers(name, type, forceRootStart);
        List<DnsName> zoneCutsTraversed = [zoneCut];

        // RFC 9156 QNAME minimization. `minimizationDisabled` starts from the setting and turns
        // on for the rest of this resolution when a server mishandles a minimized query (see
        // the fallbacks below); after that every hop sends the full name and type.
        // `currentProbeName` moves separately from `zoneCut`: a referral resets it to one label
        // past the new cut, and a NOERROR without a referral extends it by one label and asks
        // the same servers again.
        bool minimizationDisabled = !options.CurrentValue.EnableQnameMinimization;
        DnsName currentProbeName = minimizationDisabled ? name : ExtendTowardTarget(zoneCut, name);

        while (true)
        {
            List<IPAddress> addresses = await ResolveNameServerAddressesAsync(
                nameServerNames,
                hintAddresses,
                requestDnssec,
                budget,
                stoppingToken
            );

            bool isMinimizedProbe = !minimizationDisabled && currentProbeName != name;
            DnsName queryName = isMinimizedProbe ? currentProbeName : name;
            DnsResourceRecordTypeEnum queryType = isMinimizedProbe
                ? DnsResourceRecordTypeEnum.NS
                : type;
            // A minimized probe carries no ECS option: it is not the client's question, and
            // sending the client's subnet would defeat the point of minimizing.
            DnsEdnsOption probeClientSubnetOption = isMinimizedProbe ? null : clientSubnetOption;

            DnsMessage response = null;

            foreach (IPAddress address in addresses)
            {
                if (!budget.TryConsumeQuery())
                    return TryServeStale(
                            name,
                            type,
                            recordClass,
                            precedingChain,
                            precedingHops,
                            zoneCutsTraversed
                        ) ?? IterativeResolutionResult.ServFail();

                DnsMessage candidate = await upstreamClient.QueryAsync(
                    address,
                    queryName,
                    queryType,
                    recordClass,
                    requestDnssec,
                    probeClientSubnetOption,
                    stoppingToken
                );

                if (candidate is null)
                    continue;

                response = candidate;

                // An error to a minimized probe is handled below, by dropping minimization. An
                // error to the full query is one server's problem, so ask the zone's next server.
                if (isMinimizedProbe || !IsServerError(candidate.ResponseCode))
                    break;
            }

            // No name server answered. Changing the query shape would not help servers that never
            // responded.
            if (response is null)
                return TryServeStale(
                        name,
                        type,
                        recordClass,
                        precedingChain,
                        precedingHops,
                        zoneCutsTraversed
                    ) ?? IterativeResolutionResult.ServFail();

            if (isMinimizedProbe && response.ResponseCode == DnsResponseCodeEnum.ServerFailure)
            {
                // SERVFAIL to a minimized probe is treated like FORMERR/NOTIMP/REFUSED below:
                // stop minimizing and retry this hop with the full query (RFC 9156 §4.3).
                minimizationDisabled = true;
                continue;
            }

            // Every server that responded to the full query answered with an error. RFC 8767
            // serve-stale: serve a stale cached answer if there is one.
            if (!isMinimizedProbe && IsServerError(response.ResponseCode))
                return TryServeStale(
                        name,
                        type,
                        recordClass,
                        precedingChain,
                        precedingHops,
                        zoneCutsTraversed
                    ) ?? IterativeResolutionResult.ServFail();

            if (isMinimizedProbe)
            {
                // A referral is handled the same way whether or not the hop was minimized:
                // TryExtractReferral only checks that the referral point encloses `name` and is
                // deeper than `zoneCut`.
                (DnsName probeReferralZoneCut, List<DnsName> probeReferralNameServers) =
                    TryExtractReferral(response, name, zoneCut);

                if (probeReferralZoneCut is not null)
                {
                    CacheReferral(probeReferralZoneCut, probeReferralNameServers, response);
                    zoneCut = probeReferralZoneCut;
                    nameServerNames = probeReferralNameServers;
                    hintAddresses = [];
                    zoneCutsTraversed.Add(zoneCut);
                    currentProbeName = ExtendTowardTarget(zoneCut, name);

                    if (zoneCutsTraversed.Count > options.CurrentValue.MaxReferralDepth)
                        return TryServeStale(
                                name,
                                type,
                                recordClass,
                                precedingChain,
                                precedingHops,
                                zoneCutsTraversed
                            ) ?? IterativeResolutionResult.ServFail();

                    continue;
                }

                if (
                    response.ResponseCode
                    is DnsResponseCodeEnum.FormatError
                        or DnsResponseCodeEnum.NotImplemented
                        or DnsResponseCodeEnum.Refused
                )
                {
                    // The server does not handle minimized queries. RFC 9156 §4.3: stop minimizing
                    // for this resolution and retry this hop with the full name and type.
                    minimizationDisabled = true;
                    continue;
                }

                if (response.ResponseCode == DnsResponseCodeEnum.NxDomain)
                {
                    // NXDOMAIN for an ancestor should mean the target cannot exist, but some servers
                    // get this wrong, so stop minimizing and let the full query decide.
                    minimizationDisabled = true;
                    continue;
                }

                // NOERROR without a referral: no cut at this probe name, but there may be one
                // further down, so extend the probe by one label and ask again.
                currentProbeName = ExtendTowardTarget(currentProbeName, name);
                continue;
            }

            // An answer: records of the requested type at `name`, or a CNAME to chase. Only
            // records owned by `name` are taken; the AA bit is not checked.
            List<DnsResourceRecord> exactAnswers =
            [
                .. response.Answers.Where(record => record.Name == name && record.Type == type),
            ];

            if (exactAnswers.Count > 0)
            {
                List<DnsResourceRecord> dnssecRecords =
                [
                    .. response.Answers.Where(record =>
                        record.Type == DnsResourceRecordTypeEnum.RrSig
                    ),
                ];
                cache.StorePositive(name, type, recordClass, exactAnswers, dnssecRecords);

                return new IterativeResolutionResult(
                    RecursiveResolutionStatusEnum.Success,
                    [.. precedingChain, .. exactAnswers],
                    dnssecRecords,
                    null,
                    zoneCutsTraversed,
                    precedingHops
                );
            }

            List<DnsResourceRecord> cnameAnswers =
            [
                .. response.Answers.Where(record =>
                    record.Name == name && record.Type == DnsResourceRecordTypeEnum.Cname
                ),
            ];

            if (type != DnsResourceRecordTypeEnum.Cname && cnameAnswers.Count > 0)
            {
                // Keep this hop's RRSIGs with the CNAME RRset, in the cache and in the chain, so the
                // validator can check the hop against its zone.
                List<DnsResourceRecord> cnameDnssecRecords =
                [
                    .. response.Answers.Where(record =>
                        record.Type == DnsResourceRecordTypeEnum.RrSig
                    ),
                ];
                cache.StorePositive(
                    name,
                    DnsResourceRecordTypeEnum.Cname,
                    recordClass,
                    cnameAnswers,
                    cnameDnssecRecords
                );

                return await ChaseCnameAsync(
                    cnameAnswers,
                    cnameDnssecRecords,
                    zoneCut,
                    type,
                    recordClass,
                    requestDnssec,
                    budget,
                    cnameDepth,
                    precedingChain,
                    precedingHops,
                    clientSubnetOption,
                    stoppingToken
                );
            }

            if (response.ResponseCode == DnsResponseCodeEnum.NxDomain)
                return HandleNegativeResponse(
                    name,
                    type,
                    recordClass,
                    RecursiveResolutionStatusEnum.NxDomain,
                    response,
                    precedingChain,
                    precedingHops,
                    zoneCutsTraversed
                );

            // A referral: authority NS records for a zone deeper than the current cut. Follow it.
            (DnsName referralZoneCut, List<DnsName> referralNameServers) = TryExtractReferral(
                response,
                name,
                zoneCut
            );

            if (referralZoneCut is not null)
            {
                CacheReferral(referralZoneCut, referralNameServers, response);
                zoneCut = referralZoneCut;
                nameServerNames = referralNameServers;
                hintAddresses = [];
                zoneCutsTraversed.Add(zoneCut);

                if (zoneCutsTraversed.Count > options.CurrentValue.MaxReferralDepth)
                    return TryServeStale(
                            name,
                            type,
                            recordClass,
                            precedingChain,
                            precedingHops,
                            zoneCutsTraversed
                        ) ?? IterativeResolutionResult.ServFail();

                continue;
            }

            // Neither an answer nor a referral: NODATA. Without an SOA it is not cached, but it is
            // still reported as NODATA.
            return HandleNegativeResponse(
                name,
                type,
                recordClass,
                RecursiveResolutionStatusEnum.NoData,
                response,
                precedingChain,
                precedingHops,
                zoneCutsTraversed
            );
        }
    }

    /// <summary>
    ///     This method builds an <see cref="IterativeResolutionResult" /> from a fresh cache hit.
    ///     The zone cut is recovered from the cached NS sets, as the closest cached cut above the
    ///     name.
    /// </summary>
    private IterativeResolutionResult BuildResultFromCacheEntry(
        RecursiveCacheEntry entry,
        DnsName name,
        DnsResourceRecordTypeEnum type,
        IReadOnlyList<DnsResourceRecord> precedingChain,
        IReadOnlyList<CnameChainHop> precedingHops
    )
    {
        List<DnsName> zoneCutsTraversed = [FindBestNameServers(name, type, false).ZoneCut];

        return entry.IsNegative
            ? new IterativeResolutionResult(
                entry.NegativeResponseCode == DnsResponseCodeEnum.NxDomain
                    ? RecursiveResolutionStatusEnum.NxDomain
                    : RecursiveResolutionStatusEnum.NoData,
                precedingChain,
                entry.DnssecRecords,
                entry.Soa,
                zoneCutsTraversed,
                precedingHops
            )
            : new IterativeResolutionResult(
                RecursiveResolutionStatusEnum.Success,
                [.. precedingChain, .. entry.Records],
                entry.DnssecRecords,
                null,
                zoneCutsTraversed,
                precedingHops
            );
    }

    /// <summary>
    ///     This method implements RFC 8767 serve-stale: when no name server answered, every answer
    ///     was an error, the query budget ran out or the referral depth was exceeded, a cache entry
    ///     within <see cref="RecursiveServerOptions.MaxStaleServeAge" /> of expiry is used instead
    ///     of failing. The CNAME-length and malformed-CNAME failures do not try it.
    /// </summary>
    /// <returns>
    ///     A result from the stale entry, or <see langword="null" /> when
    ///     <see cref="RecursiveServerOptions.EnableServeStale" /> is off or nothing usable is
    ///     cached, in which case the caller returns <see cref="IterativeResolutionResult.ServFail" />.
    /// </returns>
    private IterativeResolutionResult TryServeStale(
        DnsName name,
        DnsResourceRecordTypeEnum type,
        DnsClassEnum recordClass,
        IReadOnlyList<DnsResourceRecord> precedingChain,
        IReadOnlyList<CnameChainHop> precedingHops,
        IReadOnlyList<DnsName> zoneCutsTraversed
    )
    {
        if (!options.CurrentValue.EnableServeStale)
            return null;

        RecursiveCacheEntry stale = cache.TryGetStale(
            name,
            type,
            recordClass,
            options.CurrentValue.MaxStaleServeAge
        );

        if (stale is null)
            return null;

        // Uses the zone cuts this attempt walked, rather than rebuilding them from the cache as
        // BuildResultFromCacheEntry does.
        return stale.IsNegative
            ? new IterativeResolutionResult(
                stale.NegativeResponseCode == DnsResponseCodeEnum.NxDomain
                    ? RecursiveResolutionStatusEnum.NxDomain
                    : RecursiveResolutionStatusEnum.NoData,
                precedingChain,
                stale.DnssecRecords,
                stale.Soa,
                zoneCutsTraversed,
                precedingHops
            )
            : new IterativeResolutionResult(
                RecursiveResolutionStatusEnum.Success,
                [.. precedingChain, .. stale.Records],
                stale.DnssecRecords,
                null,
                zoneCutsTraversed,
                precedingHops
            );
    }

    /// <summary>
    ///     This method follows a CNAME one hop by resolving its target, which may be in another
    ///     zone, so the walk starts again from the cache (or the root). Only the first CNAME record
    ///     is followed. The hop is recorded as a <see cref="CnameChainHop" /> so
    ///     <see cref="Validation.DnssecValidator" /> can check it. Exceeding
    ///     <see cref="RecursiveServerOptions.MaxCnameChainLength" /> gives SERVFAIL.
    /// </summary>
    private async Task<IterativeResolutionResult> ChaseCnameAsync(
        IReadOnlyList<DnsResourceRecord> cnameRecords,
        IReadOnlyList<DnsResourceRecord> cnameDnssecRecords,
        DnsName owningZone,
        DnsResourceRecordTypeEnum type,
        DnsClassEnum recordClass,
        bool requestDnssec,
        QueryBudget budget,
        int cnameDepth,
        IReadOnlyList<DnsResourceRecord> precedingChain,
        IReadOnlyList<CnameChainHop> precedingHops,
        DnsEdnsOption clientSubnetOption,
        CancellationToken stoppingToken
    )
    {
        if (cnameDepth >= options.CurrentValue.MaxCnameChainLength)
            return IterativeResolutionResult.ServFail();

        DnsResourceRecord cnameRecord = cnameRecords[0];

        if (
            DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.Cname, cnameRecord.Data)
            is not CnameRecordData cnameData
        )
            return IterativeResolutionResult.ServFail();

        CnameChainHop hop = new(cnameRecord, cnameDnssecRecords, owningZone);

        return await ResolveInternalAsync(
            cnameData.Target,
            type,
            recordClass,
            requestDnssec,
            budget,
            cnameDepth + 1,
            [.. precedingChain, cnameRecord],
            [.. precedingHops, hop],
            forceRootStart: false,
            clientSubnetOption,
            bypassCache: false,
            stoppingToken
        );
    }

    /// <summary>
    ///     This method builds a negative result, and caches it when the response carried the SOA
    ///     record RFC 2308 needs.
    /// </summary>
    private IterativeResolutionResult HandleNegativeResponse(
        DnsName name,
        DnsResourceRecordTypeEnum type,
        DnsClassEnum recordClass,
        RecursiveResolutionStatusEnum status,
        DnsMessage response,
        IReadOnlyList<DnsResourceRecord> precedingChain,
        IReadOnlyList<CnameChainHop> precedingHops,
        IReadOnlyList<DnsName> zoneCutsTraversed
    )
    {
        DnsResourceRecord soa = response.AuthorityRecords.FirstOrDefault(record =>
            record.Type == DnsResourceRecordTypeEnum.Soa
        );
        List<DnsResourceRecord> dnssecRecords =
        [
            .. response.AuthorityRecords.Where(record =>
                record.Type
                    is DnsResourceRecordTypeEnum.RrSig
                        or DnsResourceRecordTypeEnum.Nsec
                        or DnsResourceRecordTypeEnum.Nsec3
            ),
        ];

        if (soa is not null)
        {
            DnsResponseCodeEnum responseCode =
                status == RecursiveResolutionStatusEnum.NxDomain
                    ? DnsResponseCodeEnum.NxDomain
                    : DnsResponseCodeEnum.NoError;
            cache.StoreNegative(name, type, recordClass, responseCode, soa, dnssecRecords);
        }

        return new IterativeResolutionResult(
            status,
            precedingChain,
            dnssecRecords,
            soa,
            zoneCutsTraversed,
            precedingHops
        );
    }

    /// <summary>
    ///     This method finds the closest zone cut with cached name servers, walking up from
    ///     <paramref name="name" /> to the root.
    /// </summary>
    /// <param name="name">The name about to be queried.</param>
    /// <param name="type">
    ///     The record type about to be queried. <see cref="DnsResourceRecordTypeEnum.DS" /> is
    ///     special: a DS record lives in the parent zone (RFC 4035 §3.1.4.1), so the NS set at
    ///     <paramref name="name" /> itself must not be used, and the search starts one label up
    ///     (as <c>AuthoritativeZoneCache.FindZone</c> does for the same reason).
    /// </param>
    /// <param name="forceRootStart">
    ///     Whether to skip the walk and start from the root name servers (see
    ///     <see cref="IIterativeResolutionEngine.ResolveAsync(DnsName,DnsResourceRecordTypeEnum,DnsClassEnum,bool,QueryBudget,bool,DnsEdnsOption,CancellationToken)" />).
    /// </param>
    /// <returns>
    ///     The zone cut and its name server names. When the cache holds no root name servers, the
    ///     third item holds the root hint addresses, which the caller uses when the cache has no
    ///     address for the names; otherwise it is empty.
    /// </returns>
    private (
        DnsName ZoneCut,
        List<DnsName> NameServerNames,
        List<IPAddress> HintAddresses
    ) FindBestNameServers(DnsName name, DnsResourceRecordTypeEnum type, bool forceRootStart)
    {
        int firstLabelToSkip =
            forceRootStart ? name.Labels.Count
            : type == DnsResourceRecordTypeEnum.DS ? 1
            : 0;

        for (int start = firstLabelToSkip; start <= name.Labels.Count; start++)
        {
            DnsName candidate = new([.. name.Labels.Skip(start)]);
            RecursiveCacheEntry cached = cache.TryGet(
                candidate,
                DnsResourceRecordTypeEnum.NS,
                DnsClassEnum.Internet
            );

            if (cached is { IsNegative: false } && cached.Records.Count > 0)
                return (candidate, [.. cached.Records.Select(ExtractNameServerName)], []);
        }

        // Not even the root is cached: the entries expired (the hint TTL is six days) or the
        // cache was flushed. The hint names alone are no use, since looking up their addresses
        // needs the root, so the addresses travel with them, and the hints go back into the
        // cache for the next query.
        IReadOnlyList<RootHintServer> rootServers = RootHints.Resolve(options.CurrentValue);
        RootHintsSeeder.Seed(cache, rootServers);

        return (
            DnsName.Root,
            [.. rootServers.Select(server => DnsName.Parse(server.Name))],
            [.. rootServers.SelectMany(server => server.Addresses)]
        );
    }

    /// <summary>
    ///     This method returns the addresses of a set of name servers. It uses cached A and AAAA
    ///     records (usually glue) when any are known, then <paramref name="hintAddresses" />;
    ///     otherwise it looks up A records for up to
    ///     three of the servers, one at a time, and stops at the first that yields an address.
    /// </summary>
    private async Task<List<IPAddress>> ResolveNameServerAddressesAsync(
        IReadOnlyList<DnsName> nameServerNames,
        IReadOnlyList<IPAddress> hintAddresses,
        bool requestDnssec,
        QueryBudget budget,
        CancellationToken stoppingToken
    )
    {
        List<IPAddress> addresses = [];

        foreach (DnsName nsName in nameServerNames)
        {
            addresses.AddRange(
                ExtractAddresses(
                    cache.TryGet(nsName, DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet)
                )
            );
            addresses.AddRange(
                ExtractAddresses(
                    cache.TryGet(nsName, DnsResourceRecordTypeEnum.Aaaa, DnsClassEnum.Internet)
                )
            );
        }

        if (addresses.Count > 0)
            return addresses;

        if (hintAddresses.Count > 0)
            return [.. hintAddresses];

        // No address is known for any of these servers: look up a few, one at a time, and stop
        // at the first that yields an address. A server whose address is already being looked
        // up further up this resolution is skipped, since its lookup would lead back here (a
        // name server inside the zone it serves, with no glue).
        const int maxNameServersToResolve = 3;

        foreach (DnsName nsName in nameServerNames.Take(maxNameServersToResolve))
        {
            if (!budget.TryBeginNameServerLookup(nsName))
                continue;

            IterativeResolutionResult aResult;

            try
            {
                // No ECS option: a name server address is not the client's question (see
                // IIterativeResolutionEngine).
                aResult = await ResolveInternalAsync(
                    nsName,
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet,
                    requestDnssec,
                    budget,
                    0,
                    [],
                    [],
                    forceRootStart: false,
                    clientSubnetOption: null,
                    bypassCache: false,
                    stoppingToken
                );
            }
            finally
            {
                budget.EndNameServerLookup(nsName);
            }

            if (aResult.Status == RecursiveResolutionStatusEnum.Success)
                addresses.AddRange(ExtractAddresses(aResult.Answer));

            if (addresses.Count > 0)
                break;
        }

        return addresses;
    }

    /// <summary>
    ///     This method checks whether a response's authority section is a referral: NS records for
    ///     a zone deeper than the current cut that encloses the queried name. It returns that
    ///     zone and its name server names, or nulls.
    /// </summary>
    private static (DnsName ZoneCut, List<DnsName> NameServerNames) TryExtractReferral(
        DnsMessage response,
        DnsName queriedName,
        DnsName currentZoneCut
    )
    {
        List<DnsResourceRecord> nsRecords =
        [
            .. response.AuthorityRecords.Where(record =>
                record.Type == DnsResourceRecordTypeEnum.NS
            ),
        ];

        if (nsRecords.Count == 0)
            return (null, null);

        DnsName referralPoint = nsRecords[0].Name;

        bool isStrictlyDeeper =
            referralPoint.Labels.Count > currentZoneCut.Labels.Count
            && IsSuffixOf(currentZoneCut, referralPoint);
        bool isEnclosingQueriedName = IsSuffixOf(referralPoint, queriedName);

        if (!isStrictlyDeeper || !isEnclosingQueriedName)
            return (null, null);

        List<DnsName> nameServerNames =
        [
            .. nsRecords
                .Where(record => record.Name == referralPoint)
                .Select(ExtractNameServerName),
        ];

        return (referralPoint, nameServerNames);
    }

    /// <summary>
    ///     This method caches a referral's NS set (with a fixed one-hour TTL, not the referral's)
    ///     and the glue for name servers inside the delegated zone. Glue for names outside it is
    ///     discarded, since the parent has no authority over those names; their addresses are
    ///     looked up separately when needed.
    /// </summary>
    private void CacheReferral(DnsName zoneCut, List<DnsName> nameServerNames, DnsMessage response)
    {
        List<DnsResourceRecord> nsRecords =
        [
            .. nameServerNames.Select(nsName => new DnsResourceRecord(
                zoneCut,
                DnsResourceRecordTypeEnum.NS,
                DnsClassEnum.Internet,
                3600,
                DnsRecordDataCodecRegistry.Encode(
                    DnsResourceRecordTypeEnum.NS,
                    new NsRecordData(nsName)
                )
            )),
        ];
        cache.StorePositive(
            zoneCut,
            DnsResourceRecordTypeEnum.NS,
            DnsClassEnum.Internet,
            nsRecords,
            []
        );

        foreach (DnsName nsName in nameServerNames)
        {
            if (!IsSuffixOf(zoneCut, nsName))
                continue;

            List<DnsResourceRecord> glueA =
            [
                .. response.AdditionalRecords.Where(record =>
                    record.Name == nsName && record.Type == DnsResourceRecordTypeEnum.A
                ),
            ];
            List<DnsResourceRecord> glueAaaa =
            [
                .. response.AdditionalRecords.Where(record =>
                    record.Name == nsName && record.Type == DnsResourceRecordTypeEnum.Aaaa
                ),
            ];

            if (glueA.Count > 0)
                cache.StorePositive(
                    nsName,
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet,
                    glueA,
                    []
                );

            if (glueAaaa.Count > 0)
                cache.StorePositive(
                    nsName,
                    DnsResourceRecordTypeEnum.Aaaa,
                    DnsClassEnum.Internet,
                    glueAaaa,
                    []
                );
        }
    }

    /// <summary>
    ///     This method checks whether a response code means the server could not or would not
    ///     answer (a broken or lame server), rather than saying anything about the name.
    /// </summary>
    private static bool IsServerError(DnsResponseCodeEnum responseCode) =>
        responseCode
            is DnsResponseCodeEnum.ServerFailure
                or DnsResponseCodeEnum.Refused
                or DnsResponseCodeEnum.NotImplemented
                or DnsResponseCodeEnum.FormatError;

    /// <summary>
    ///     This method checks whether one name is a suffix of (or equal to) another, the
    ///     bailiwick test used throughout this class.
    /// </summary>
    private static bool IsSuffixOf(DnsName suffix, DnsName name)
    {
        if (suffix.Labels.Count > name.Labels.Count)
            return false;

        DnsName trimmed = new([.. name.Labels.Skip(name.Labels.Count - suffix.Labels.Count)]);

        return trimmed == suffix;
    }

    /// <summary>
    ///     This method builds an RFC 9156 minimized query name: the rightmost labels of
    ///     <paramref name="target" />, one more than <paramref name="current" /> has, or
    ///     <paramref name="target" /> itself once <paramref name="current" /> is as long. It builds
    ///     the first probe past a new zone cut and extends a probe that found no cut.
    /// </summary>
    private static DnsName ExtendTowardTarget(DnsName current, DnsName target)
    {
        if (current.Labels.Count >= target.Labels.Count)
            return target;

        int labelsToKeep = current.Labels.Count + 1;

        return new DnsName([.. target.Labels.Skip(target.Labels.Count - labelsToKeep)]);
    }

    /// <summary>
    ///     This method decodes an NS record's name server name.
    /// </summary>
    private static DnsName ExtractNameServerName(DnsResourceRecord record) =>
        (
            (NsRecordData)
                DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.NS, record.Data)
        ).NameServer;

    /// <summary>
    ///     This method extracts every address from a cache entry's A/AAAA records.
    /// </summary>
    private static List<IPAddress> ExtractAddresses(RecursiveCacheEntry entry) =>
        entry is { IsNegative: false } ? ExtractAddresses(entry.Records) : [];

    /// <summary>
    ///     This method extracts every address from a set of A/AAAA records.
    /// </summary>
    private static List<IPAddress> ExtractAddresses(IEnumerable<DnsResourceRecord> records) =>
        [
            .. records
                .Where(record =>
                    record.Type is DnsResourceRecordTypeEnum.A or DnsResourceRecordTypeEnum.Aaaa
                )
                .Select(record =>
                    record.Type == DnsResourceRecordTypeEnum.A
                        ? (
                            (ARecordData)
                                DnsRecordDataCodecRegistry.Decode(
                                    DnsResourceRecordTypeEnum.A,
                                    record.Data
                                )
                        ).Address
                        : (
                            (AaaaRecordData)
                                DnsRecordDataCodecRegistry.Decode(
                                    DnsResourceRecordTypeEnum.Aaaa,
                                    record.Data
                                )
                        ).Address
                ),
        ];
}
