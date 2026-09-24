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
///     This class implements <see cref="IIterativeResolutionEngine" />: the classic RFC 1035 §7
///     algorithm -- start from the closest zone cut this resolver already has cached name
///     servers for (falling back to the root), query one, and either accept its answer, follow
///     its referral one zone cut deeper, or give up and try a different name server.
/// </summary>
/// <param name="cache">The cache this engine reads name servers and answers from, and writes them back to.</param>
/// <param name="upstreamClient">The client used to actually send a query to a chosen name server.</param>
/// <param name="options">
///     This engine's own bounds (referral depth, CNAME chain length, query budget), read live via
///     <see cref="IOptionsMonitor{TOptions}" /> -- see <see cref="Resolution.RecursiveQueryResolver" />'s
///     own doc comment for why this engine, registered as a singleton, cannot use the usual
///     snapshotted <see cref="IOptions{TOptions}" /> instead.
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

        // bypassCache: true is what makes this genuinely different from an ordinary
        // ResolveAsync call -- a prefetch candidate is, by construction (see
        // RecursiveCache.GetPrefetchCandidates), still fresh enough that the ordinary
        // cachedExact short-circuit below would just hand back the same not-yet-expired entry
        // unchanged, defeating the entire point of refreshing it before it actually expires.
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
    ///     This method does the actual work, recursing into itself once per CNAME hop followed.
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
        // A cached exact answer (positive or negative) short-circuits everything below. Its own
        // zone-cut provenance was never recorded alongside it, but the same cache walk
        // FindBestNameServers already does to pick a starting point recovers exactly the same
        // answer -- needed so a cache hit's own IterativeResolutionResult remains just as usable
        // for DNSSEC chain-of-trust validation as a freshly-walked one, rather than silently
        // losing which zone the cached data actually belongs to. Skipped entirely when
        // bypassCache is set -- see PrefetchAsync's own doc comment for why active cache
        // prefetching is the one caller that must never take this shortcut.
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

        // A cached CNAME at this name, even though we were not asked for one, means this name's
        // only real data *is* the alias -- chase it before ever going out to the network.
        if (type != DnsResourceRecordTypeEnum.Cname)
        {
            RecursiveCacheEntry cachedCname = cache.TryGet(
                name,
                DnsResourceRecordTypeEnum.Cname,
                recordClass
            );

            if (cachedCname is { IsNegative: false })
            {
                // A cache hit carries no zone-cut provenance of its own (see BuildResultFromCacheEntry's
                // own doc comment for why) -- the same cache walk FindBestNameServers already does
                // recovers exactly which zone this cached CNAME RRset actually belongs to, needed so
                // DnssecValidator can build a fresh chain of trust to that specific zone for this hop.
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

        (DnsName zoneCut, List<DnsName> nameServerNames) = FindBestNameServers(
            name,
            type,
            forceRootStart
        );
        List<DnsName> zoneCutsTraversed = [zoneCut];

        // RFC 9156 QNAME minimization state: `minimizationDisabled` starts from this engine's own
        // configured toggle, but can also flip permanently mid-resolution the moment a server
        // shows any sign of mishandling a minimized query (see the fallback triggers below) --
        // once disabled, every remaining hop of this one resolution sends the ordinary, full
        // name/type, exactly as if minimization had never been attempted at all.
        // `currentProbeName` tracks progress independently of `zoneCut`: a genuine referral resets
        // it to a fresh one-label-past-the-new-cut probe, while a NOERROR/no-referral response at
        // the current probe name (no deeper delegation exists there, but the real target might
        // still have one further down) extends it by one more label and asks the very same server
        // again, without ever touching `zoneCut` itself.
        bool minimizationDisabled = !options.CurrentValue.EnableQnameMinimization;
        DnsName currentProbeName = minimizationDisabled ? name : ExtendTowardTarget(zoneCut, name);

        while (true)
        {
            List<IPAddress> addresses = await ResolveNameServerAddressesAsync(
                nameServerNames,
                requestDnssec,
                budget,
                stoppingToken
            );

            bool isMinimizedProbe = !minimizationDisabled && currentProbeName != name;
            DnsName queryName = isMinimizedProbe ? currentProbeName : name;
            DnsResourceRecordTypeEnum queryType = isMinimizedProbe
                ? DnsResourceRecordTypeEnum.NS
                : type;
            // A minimized probe deliberately never carries the client's own EDNS Client Subnet
            // option -- it is internal plumbing toward answering the real question, not the
            // question itself, the same "internal lookups don't leak ECS" precedent
            // ResolveNameServerAddressesAsync's own doc comment already establishes, and
            // forwarding it here would defeat minimization's whole point of limiting what an
            // intermediate server ever learns about the client-driven query.
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

                response = await upstreamClient.QueryAsync(
                    address,
                    queryName,
                    queryType,
                    recordClass,
                    requestDnssec,
                    probeClientSubnetOption,
                    stoppingToken
                );

                if (response is not null)
                    break;
            }

            // No reachable name server answered at all -- true regardless of whether this hop was
            // a minimized probe or the real query, and retrying with a different query shape
            // against servers that never responded at all would not help either way.
            if (response is null)
                return TryServeStale(
                        name,
                        type,
                        recordClass,
                        precedingChain,
                        precedingHops,
                        zoneCutsTraversed
                    ) ?? IterativeResolutionResult.ServFail();

            if (response.ResponseCode == DnsResponseCodeEnum.ServerFailure)
            {
                if (isMinimizedProbe)
                {
                    // A server that cannot even answer a minimized probe (rather than a clean
                    // FORMERR/NOTIMP/REFUSED) is treated exactly like those signals below -- RFC
                    // 9156 §4.3's same documented fallback: give up on minimization for the rest
                    // of this resolution and retry the very same hop with the ordinary, full
                    // query, rather than failing the whole lookup over what may be nothing more
                    // than this one server's own minimization support gap.
                    minimizationDisabled = true;
                    continue;
                }

                // RFC 8767 "serve stale": every reachable name server answered SERVFAIL to the
                // real query -- if a (possibly expired, but still within this resolver's own
                // configured grace window) cached answer exists, it is served instead of failing
                // this query outright.
                return TryServeStale(
                        name,
                        type,
                        recordClass,
                        precedingChain,
                        precedingHops,
                        zoneCutsTraversed
                    ) ?? IterativeResolutionResult.ServFail();
            }

            if (isMinimizedProbe)
            {
                // A genuine referral is handled identically whether this hop was minimized or
                // not: `TryExtractReferral` only cares that the referral point is a real suffix
                // of the eventual target `name` and strictly deeper than `zoneCut`, both of which
                // hold regardless of what exact name this probe asked about.
                (DnsName probeReferralZoneCut, List<DnsName> probeReferralNameServers) =
                    TryExtractReferral(response, name, zoneCut);

                if (probeReferralZoneCut is not null)
                {
                    CacheReferral(probeReferralZoneCut, probeReferralNameServers, response);
                    zoneCut = probeReferralZoneCut;
                    nameServerNames = probeReferralNameServers;
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
                    // A clean signal that this server does not understand -- or refuses -- a
                    // minimized query at all. RFC 9156 §4.3's documented fallback: abandon
                    // minimization for the remainder of this one resolution and retry this exact
                    // hop with the ordinary, full name/type instead.
                    minimizationDisabled = true;
                    continue;
                }

                if (response.ResponseCode == DnsResponseCodeEnum.NxDomain)
                {
                    // An NXDOMAIN for an intermediate, minimized ancestor name is real evidence
                    // the full target name cannot exist either -- except real-world wildcard and
                    // CDN configurations make that inference genuinely unreliable in practice.
                    // Rather than risk a false negative on a real answer, fall back to an
                    // ordinary, full, non-minimized query and let its own real response (a real
                    // NXDOMAIN, this time actually naming the queried name itself) settle it.
                    minimizationDisabled = true;
                    continue;
                }

                // NOERROR with no referral at this exact probe name: no deeper delegation exists
                // here, but the real target name may still have one further down -- extend the
                // probe by one more label and ask this very same server again, without ever
                // moving `zoneCut`.
                currentProbeName = ExtendTowardTarget(currentProbeName, name);
                continue;
            }

            // A real answer: either exactly the requested type, or a CNAME to chase.
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
                // This hop's own RRSIG (if DNSSEC data was requested/received) is captured and
                // cached alongside the CNAME RRset itself -- previously discarded here entirely,
                // which is exactly why a cross-zone-cut CNAME chain's own intermediate hop could
                // never actually be authenticated: there was never any signature left to check it
                // against, even from a fresh (non-cached) lookup.
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

            // A referral: NS records, in the Authority section, for a zone strictly deeper than
            // the one we just asked -- go one level deeper and try again.
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

            // Neither an answer nor a referral: NODATA if an authoritative SOA came back,
            // otherwise there is nothing left to do but give up.
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
    ///     This method builds an <see cref="IterativeResolutionResult" /> straight from a cache
    ///     hit -- shared by the ordinary "still-fresh cache hit" short-circuit at the top of
    ///     <see cref="ResolveInternalAsync" /> and by <see cref="TryServeStale" />'s own
    ///     RFC 8767 stale-serving fallback, since both need to turn a <see cref="RecursiveCacheEntry" />
    ///     into the exact same result shape.
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
    ///     This method implements RFC 8767 "serve stale": when every reachable name server for
    ///     <paramref name="name" /> either failed to answer at all or answered with SERVFAIL, an
    ///     expired-but-still-within-<see cref="RecursiveServerOptions.MaxStaleServeAge" /> cache
    ///     entry is served instead of failing this query outright -- real availability during a
    ///     genuine upstream/authoritative outage, at the cost of serving an answer this resolver
    ///     already knows may no longer be current. Every caller within
    ///     <see cref="ResolveInternalAsync" /> that would otherwise return
    ///     <see cref="IterativeResolutionResult.ServFail" /> tries this first.
    /// </summary>
    /// <returns>
    ///     A stale-but-usable result, or <see langword="null" /> when this feature is disabled
    ///     (<see cref="RecursiveServerOptions.EnableServeStale" />) or no cache entry exists at
    ///     all, even an expired one -- in which case the caller's own
    ///     <see cref="IterativeResolutionResult.ServFail" /> fallback still applies.
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

        // Reuses zoneCutsTraversed (the real walk this attempt actually took) rather than
        // BuildResultFromCacheEntry's own re-derived cachedZoneCutsTraversed -- this call already
        // has the real one in hand, and it is at least as accurate.
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
    ///     This method follows a CNAME one hop further, recursing into
    ///     <see cref="ResolveInternalAsync" /> for the target name -- which may land in a
    ///     completely different zone, so the referral walk starts fresh from whatever is already
    ///     cached for it (or the root). This hop's own owning zone and signature material are
    ///     recorded into a fresh <see cref="CnameChainHop" />, appended to the chain the eventual
    ///     result carries, so <see cref="Validation.DnssecValidator" /> can authenticate this hop
    ///     independently rather than only the chain's terminal answer.
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
    ///     This method builds a negative result, caching it too when the response actually
    ///     carried the SOA record RFC 2308 needs to do so.
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
    ///     This method finds the closest zone cut this resolver already has cached name servers
    ///     for, walking upward from <paramref name="name" /> toward the root.
    /// </summary>
    /// <param name="name">The name about to be queried.</param>
    /// <param name="type">
    ///     The record type about to be queried -- <see cref="DnsResourceRecordTypeEnum.DS" />
    ///     is special: per RFC 4035 §3.1.4.1, a DS record lives only in the *parent* zone, so an
    ///     exact-match cache entry at <paramref name="name" /> itself (its own zone cut, if it
    ///     happens to be a delegation point too) must never be used -- the search always starts
    ///     one level up, at the immediate parent, the same way the authoritative side's own
    ///     <c>AuthoritativeZoneCache.FindZone</c> delegation fix handles the identical
    ///     NS/DS-at-a-delegation-point ambiguity.
    /// </param>
    /// <param name="forceRootStart">
    ///     Whether to skip this cache walk entirely and always start from the root zone's own
    ///     name servers -- see <see cref="IIterativeResolutionEngine.ResolveAsync(DnsName,DnsResourceRecordTypeEnum,DnsClassEnum,bool,QueryBudget,bool,DnsEdnsOption,CancellationToken)" />.
    /// </param>
    private (DnsName ZoneCut, List<DnsName> NameServerNames) FindBestNameServers(
        DnsName name,
        DnsResourceRecordTypeEnum type,
        bool forceRootStart
    )
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
                return (candidate, [.. cached.Records.Select(ExtractNameServerName)]);
        }

        // Nothing at all is cached yet, not even the root -- this should only happen before
        // RootHintsPrimingService has ever run once; fall back to the hardcoded list directly.
        return (
            DnsName.Root,
            [
                .. RootHints
                    .Resolve(options.CurrentValue)
                    .Select(server => DnsName.Parse(server.Name)),
            ]
        );
    }

    /// <summary>
    ///     This method resolves a set of name servers' own addresses, preferring whatever is
    ///     already cached (glue, most commonly) and only issuing a fresh, nested lookup -- for
    ///     at most a handful of candidates -- when nothing at all is already known.
    /// </summary>
    private async Task<List<IPAddress>> ResolveNameServerAddressesAsync(
        IReadOnlyList<DnsName> nameServerNames,
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

        // Nothing at all is known for any name server in this set -- resolve a bounded handful
        // of them, one at a time, stopping as soon as one actually yields an address.
        const int maxNameServersToResolve = 3;

        foreach (DnsName nsName in nameServerNames.Take(maxNameServersToResolve))
        {
            // Deliberately no clientSubnetOption here: resolving a name server's own address is
            // internal plumbing to answer the client's real question, not the question itself --
            // see IIterativeResolutionEngine's own doc comment for why ECS is scoped only to the
            // client-driven query chain (the original name and any CNAME target), not every
            // nested lookup a resolution happens to need along the way.
            IterativeResolutionResult aResult = await ResolveInternalAsync(
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

            if (aResult.Status == RecursiveResolutionStatusEnum.Success)
                addresses.AddRange(ExtractAddresses(aResult.Answer));

            if (addresses.Count > 0)
                break;
        }

        return addresses;
    }

    /// <summary>
    ///     This method checks whether a response's Authority section carries a genuine referral
    ///     -- NS records for a zone strictly deeper than the one just queried -- and, if so,
    ///     extracts its own zone cut name and name server names.
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
    ///     This method caches a referral's own NS RRset, plus whatever glue from its Additional
    ///     section is actually in-bailiwick -- an address for a name server that itself falls
    ///     under the zone being delegated. Out-of-bailiwick glue is discarded entirely rather
    ///     than trusted directly (an off-path name server has no business supplying an address
    ///     for a name outside the zone it was just delegated); that name server's address is
    ///     instead resolved through its own, independent lookup the next time it is actually
    ///     needed.
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
    ///     This method checks whether one name is a suffix of another (or equal to it) -- the
    ///     bailiwick/ancestor test used throughout this class.
    /// </summary>
    private static bool IsSuffixOf(DnsName suffix, DnsName name)
    {
        if (suffix.Labels.Count > name.Labels.Count)
            return false;

        DnsName trimmed = new([.. name.Labels.Skip(name.Labels.Count - suffix.Labels.Count)]);

        return trimmed == suffix;
    }

    /// <summary>
    ///     This method builds RFC 9156's own minimized query name: <paramref name="target" />'s
    ///     rightmost labels, exactly one label longer than <paramref name="current" /> -- "the
    ///     next label down from what is already known, plus the already-known suffix itself" --
    ///     or <paramref name="target" /> unchanged once <paramref name="current" /> already has as
    ///     many (or more) labels. Used both to build a fresh probe name one label past a newly
    ///     discovered zone cut, and to extend an existing probe by one further label when a server
    ///     has already confirmed no deeper delegation exists at the probe's current length.
    /// </summary>
    private static DnsName ExtendTowardTarget(DnsName current, DnsName target)
    {
        if (current.Labels.Count >= target.Labels.Count)
            return target;

        int labelsToKeep = current.Labels.Count + 1;

        return new DnsName([.. target.Labels.Skip(target.Labels.Count - labelsToKeep)]);
    }

    /// <summary>
    ///     This method decodes an NS record's own name server name.
    /// </summary>
    private static DnsName ExtractNameServerName(DnsResourceRecord record) =>
        (
            (NsRecordData)
                DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.NS, record.Data)
        ).NameServer;

    /// <summary>
    ///     This method extracts every address from a cache entry's own A/AAAA records.
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
