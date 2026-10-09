using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Core.Caching;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Forwarding.Caching;
using Ninlil.Dns.Server.Forwarding.Tests.Fakes;
using Ninlil.Dns.Server.Recursive;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Tests;

/// <summary>
///     This class unit-tests <see cref="ForwardedResponseCache" /> and
///     <see cref="ForwardedResponseCacheKey" /> directly, with a hand-moved clock: what a key
///     distinguishes, how long each kind of answer lives, TTL decrement, what is never cached,
///     serve-stale, the size bound, flushing and single-name removal.
/// </summary>
public class ForwardedResponseCacheTests
{
    private static readonly string[] GlobalForwarders = ["9.9.9.9", "149.112.112.112"];

    private readonly ManualTimeProvider _clock = new();
    private readonly RecordingEventPublisher _publisher = new();

    private readonly ForwardingOptions _forwardingOptions = new()
    {
        EnableCache = true,
        MaxCacheEntries = 100,
    };

    private readonly RecursiveServerOptions _recursiveOptions = new() { EnableServeStale = false };

    private ForwardedResponseCache CreateCache() =>
        new(
            new StaticOptionsMonitor<ForwardingOptions>(_forwardingOptions),
            new StaticOptionsMonitor<RecursiveServerOptions>(_recursiveOptions),
            _publisher,
            _clock
        );

    private static DnsMessage Query(
        string name,
        DnsResourceRecordTypeEnum type = DnsResourceRecordTypeEnum.A,
        ushort id = 1,
        bool dnssecOk = false,
        bool checkingDisabled = false,
        bool edns = true
    ) =>
        new()
        {
            Id = id,
            Opcode = DnsOpcodeEnum.Query,
            RecursionDesired = true,
            CheckingDisabled = checkingDisabled,
            Questions = { new DnsQuestion(DnsName.Parse(name), type, DnsClassEnum.Internet) },
            Edns = edns ? new DnsEdnsOptions(0, 1232, dnssecOk, []) : null,
        };

    private static ForwardedResponseCacheKey Key(
        DnsMessage request,
        IReadOnlyList<string> upstreams = null,
        DnsMessage outbound = null
    ) =>
        ForwardedResponseCacheKey.Create(
            request,
            outbound ?? request,
            upstreams ?? GlobalForwarders,
            validated: false
        );

    private static DnsResourceRecord A(string name, uint ttl, string address = "192.0.2.1") =>
        new(
            DnsName.Parse(name),
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            ttl,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.A,
                new ARecordData(IPAddress.Parse(address))
            )
        );

    private static DnsResourceRecord Cname(string name, string target, uint ttl) =>
        new(
            DnsName.Parse(name),
            DnsResourceRecordTypeEnum.Cname,
            DnsClassEnum.Internet,
            ttl,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.Cname,
                new CnameRecordData(DnsName.Parse(target))
            )
        );

    private static DnsResourceRecord Soa(string zone, uint ttl, uint minimum) =>
        new(
            DnsName.Parse(zone),
            DnsResourceRecordTypeEnum.Soa,
            DnsClassEnum.Internet,
            ttl,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.Soa,
                new SoaRecordData(
                    DnsName.Parse("ns1." + zone),
                    DnsName.Parse("hostmaster." + zone),
                    1,
                    3600,
                    900,
                    604800,
                    minimum
                )
            )
        );

    private static DnsMessage Response(
        DnsResponseCodeEnum code,
        IEnumerable<DnsResourceRecord> answers = null,
        IEnumerable<DnsResourceRecord> authority = null
    )
    {
        DnsMessage response = new()
        {
            IsResponse = true,
            RecursionAvailable = true,
            ResponseCode = code,
        };
        response.Answers.AddRange(answers ?? []);
        response.AuthorityRecords.AddRange(authority ?? []);
        return response;
    }

    private static DnsMessage Positive(string name, uint ttl = 300) =>
        Response(DnsResponseCodeEnum.NoError, [A(name, ttl)]);

    private static DnsResourceRecord Signature(
        string name,
        DnsResourceRecordTypeEnum covered,
        uint ttl = 300
    ) =>
        new(
            DnsName.Parse(name),
            DnsResourceRecordTypeEnum.RrSig,
            DnsClassEnum.Internet,
            ttl,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.RrSig,
                new RrSigRecordData(
                    covered,
                    Ninlil.Dns.Common.Enum.DnssecAlgorithmEnum.Ed25519,
                    2,
                    ttl,
                    2_000_000_000,
                    1_000_000_000,
                    1234,
                    DnsName.Parse("example.com"),
                    new byte[64]
                )
            )
        );

    /// <summary>
    ///     This method builds an upstream answer as a validating-forwarder gets it: an A record,
    ///     its RRSIG, an NSEC proof in authority, the upstream's AD bit and an OPT record with
    ///     options.
    /// </summary>
    private static DnsMessage SignedUpstreamAnswer()
    {
        DnsMessage response = Response(
            DnsResponseCodeEnum.NoError,
            [A("example.com", 300), Signature("example.com", DnsResourceRecordTypeEnum.A)],
            [Signature("example.com", DnsResourceRecordTypeEnum.Nsec)]
        );
        response.AuthenticData = true;
        response.Edns = new DnsEdnsOptions(
            0,
            4096,
            true,
            [new DnsEdnsOption((ushort)EdnsOptionCodeEnum.Cookie, new byte[16])]
        );

        return response;
    }

    // --- Keys -------------------------------------------------------------------------------

    [Fact]
    public void Key_IgnoresNameCaseAndTheTransactionId()
    {
        Assert.Equal(Key(Query("www.example.com", id: 1)), Key(Query("WwW.ExAmPlE.CoM.", id: 999)));
    }

    [Fact]
    public void Key_DistinguishesTheDoBitTheCdBitAndEdnsPresence()
    {
        ForwardedResponseCacheKey plain = Key(Query("example.com"));

        Assert.NotEqual(plain, Key(Query("example.com", dnssecOk: true)));
        Assert.NotEqual(plain, Key(Query("example.com", checkingDisabled: true)));
        Assert.NotEqual(plain, Key(Query("example.com", edns: false)));
    }

    [Fact]
    public void Key_DistinguishesTypeUpstreamSetValidationAndClientSubnet()
    {
        DnsMessage request = Query("example.com");
        ForwardedResponseCacheKey plain = Key(request);

        Assert.NotEqual(plain, Key(Query("example.com", DnsResourceRecordTypeEnum.Aaaa)));
        Assert.NotEqual(plain, Key(request, upstreams: ["10.0.0.53"]));
        Assert.NotEqual(
            plain,
            ForwardedResponseCacheKey.Create(request, request, GlobalForwarders, validated: true)
        );

        DnsMessage withSubnet = new()
        {
            Id = request.Id,
            Opcode = request.Opcode,
            RecursionDesired = true,
            Questions = { request.Questions[0] },
            Edns = new DnsEdnsOptions(
                0,
                1232,
                false,
                [
                    new DnsEdnsOption(
                        (ushort)EdnsOptionCodeEnum.ClientSubnet,
                        [0, 1, 24, 0, 10, 0, 0]
                    ),
                ]
            ),
        };
        ForwardedResponseCacheKey subnetKey = Key(request, outbound: withSubnet);
        Assert.NotEqual(plain, subnetKey);
        Assert.Equal("000118000A0000", subnetKey.ClientSubnet);
    }

    [Fact]
    public void Lookup_WithTheOtherDoBit_Misses()
    {
        ForwardedResponseCache cache = CreateCache();
        cache.TryStore(Key(Query("example.com")), Positive("example.com"));

        Assert.Null(cache.TryGet(Key(Query("example.com", dnssecOk: true)), Query("example.com")));
        Assert.Null(
            cache.TryGet(
                Key(Query("example.com", checkingDisabled: true)),
                Query("example.com", checkingDisabled: true)
            )
        );
        Assert.NotNull(cache.TryGet(Key(Query("EXAMPLE.com")), Query("EXAMPLE.com")));
    }

    // --- Positive answers and TTLs -------------------------------------------------------------

    [Fact]
    public void Hit_CarriesTheRequestsIdAndQuestionSpelling()
    {
        ForwardedResponseCache cache = CreateCache();
        cache.TryStore(Key(Query("www.example.com")), Positive("www.example.com"));

        DnsMessage request = Query("WwW.eXaMpLe.CoM", id: 4242);
        DnsMessage hit = cache.TryGet(Key(request), request);

        Assert.Equal(4242, hit.Id);
        Assert.True(hit.IsResponse);
        Assert.Equal("WwW.eXaMpLe.CoM.", hit.Questions[0].Name.ToString());
        Assert.Single(hit.Answers);
    }

    [Fact]
    public void Positive_LivesForTheLowestTtlInTheAnswer_AndTtlsCountDown()
    {
        ForwardedResponseCache cache = CreateCache();
        DnsMessage request = Query("www.example.com");
        DnsMessage upstream = Response(
            DnsResponseCodeEnum.NoError,
            [Cname("www.example.com", "cdn.example.net", 3600), A("cdn.example.net", 300)]
        );

        Assert.True(cache.TryStore(Key(request), upstream));

        _clock.Advance(TimeSpan.FromSeconds(100));
        DnsMessage hit = cache.TryGet(Key(request), request);
        Assert.Equal(3500u, hit.Answers[0].TimeToLive);
        Assert.Equal(200u, hit.Answers[1].TimeToLive);

        _clock.Advance(TimeSpan.FromSeconds(199));
        Assert.Equal(1u, cache.TryGet(Key(request), request).Answers[1].TimeToLive);

        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(cache.TryGet(Key(request), request));
    }

    [Fact]
    public void Positive_IsCappedAtSevenDays()
    {
        TimeSpan? lifetime = ForwardedResponseCache.ComputeLifetime(
            Query("example.com").Questions[0],
            Positive("example.com", ttl: 30_000_000),
            _clock.GetUtcNow()
        );

        Assert.Equal(TimeSpan.FromDays(7), lifetime);
    }

    [Fact]
    public void Hit_IsACopyTheCallerMayChange()
    {
        ForwardedResponseCache cache = CreateCache();
        DnsMessage request = Query("example.com");
        DnsMessage upstream = Positive("example.com");
        cache.TryStore(Key(request), upstream);

        // What the pipeline does to an oversized UDP answer, and to the original afterwards.
        upstream.Answers.Clear();
        DnsMessage first = cache.TryGet(Key(request), request);
        first.Answers.Clear();
        first.IsTruncated = true;

        DnsMessage second = cache.TryGet(Key(request), request);
        Assert.Single(second.Answers);
        Assert.False(second.IsTruncated);
    }

    // --- Negative answers ----------------------------------------------------------------------

    [Fact]
    public void NxDomain_LivesForTheSoaMinimumWhenThatIsLowerThanTheSoaTtl()
    {
        TimeSpan? lifetime = ForwardedResponseCache.ComputeLifetime(
            Query("missing.example.com").Questions[0],
            Response(DnsResponseCodeEnum.NxDomain, authority: [Soa("example.com", 3600, 60)]),
            _clock.GetUtcNow()
        );

        Assert.Equal(TimeSpan.FromSeconds(60), lifetime);
    }

    [Fact]
    public void NxDomain_LivesForTheSoaTtlWhenThatIsLowerThanTheMinimum()
    {
        TimeSpan? lifetime = ForwardedResponseCache.ComputeLifetime(
            Query("missing.example.com").Questions[0],
            Response(DnsResponseCodeEnum.NxDomain, authority: [Soa("example.com", 45, 900)]),
            _clock.GetUtcNow()
        );

        Assert.Equal(TimeSpan.FromSeconds(45), lifetime);
    }

    [Fact]
    public void Negative_IsCappedAtThreeHours()
    {
        TimeSpan? lifetime = ForwardedResponseCache.ComputeLifetime(
            Query("missing.example.com").Questions[0],
            Response(DnsResponseCodeEnum.NxDomain, authority: [Soa("example.com", 86400, 86400)]),
            _clock.GetUtcNow()
        );

        Assert.Equal(TimeSpan.FromHours(3), lifetime);
    }

    [Fact]
    public void NoData_IsCachedAndServedWithItsSoa()
    {
        ForwardedResponseCache cache = CreateCache();
        DnsMessage request = Query("example.com", DnsResourceRecordTypeEnum.Aaaa);
        DnsMessage upstream = Response(
            DnsResponseCodeEnum.NoError,
            authority: [Soa("example.com", 3600, 300)]
        );

        Assert.True(cache.TryStore(Key(request), upstream));

        _clock.Advance(TimeSpan.FromSeconds(10));
        DnsMessage hit = cache.TryGet(Key(request), request);
        Assert.Equal(DnsResponseCodeEnum.NoError, hit.ResponseCode);
        Assert.Empty(hit.Answers);
        Assert.Equal(3590u, Assert.Single(hit.AuthorityRecords).TimeToLive);

        _clock.Advance(TimeSpan.FromSeconds(290));
        Assert.Null(cache.TryGet(Key(request), request));
    }

    [Fact]
    public void CnameToNoData_LivesNoLongerThanTheCname()
    {
        TimeSpan? lifetime = ForwardedResponseCache.ComputeLifetime(
            Query("www.example.com", DnsResourceRecordTypeEnum.Aaaa).Questions[0],
            Response(
                DnsResponseCodeEnum.NoError,
                [Cname("www.example.com", "cdn.example.net", 20)],
                [Soa("example.net", 3600, 300)]
            ),
            _clock.GetUtcNow()
        );

        Assert.Equal(TimeSpan.FromSeconds(20), lifetime);
    }

    [Fact]
    public void Negative_WithoutAnSoa_IsNotCached()
    {
        ForwardedResponseCache cache = CreateCache();
        DnsMessage request = Query("missing.example.com");

        Assert.False(cache.TryStore(Key(request), Response(DnsResponseCodeEnum.NxDomain)));
        Assert.False(cache.TryStore(Key(request), Response(DnsResponseCodeEnum.NoError)));
        Assert.Null(cache.TryGet(Key(request), request));
    }

    // --- Never cached --------------------------------------------------------------------------

    [Theory]
    [InlineData(DnsResponseCodeEnum.ServerFailure)]
    [InlineData(DnsResponseCodeEnum.Refused)]
    [InlineData(DnsResponseCodeEnum.FormatError)]
    [InlineData(DnsResponseCodeEnum.NotImplemented)]
    public void ErrorResponseCodes_AreNeverCached(DnsResponseCodeEnum code)
    {
        ForwardedResponseCache cache = CreateCache();
        DnsMessage request = Query("example.com");

        // Even carrying records that would otherwise bound a lifetime.
        Assert.False(
            cache.TryStore(
                Key(request),
                Response(code, [A("example.com", 300)], [Soa("example.com", 300, 300)])
            )
        );
        Assert.Null(cache.TryGet(Key(request), request));
        Assert.Equal(0, cache.GetStatistics().EntryCount);
    }

    [Fact]
    public void TruncatedAnswers_AreNeverCached()
    {
        ForwardedResponseCache cache = CreateCache();
        DnsMessage upstream = Positive("example.com");
        upstream.IsTruncated = true;

        Assert.False(cache.TryStore(Key(Query("example.com")), upstream));
    }

    [Fact]
    public void ZeroTtlAnswers_AreNeverCached()
    {
        ForwardedResponseCache cache = CreateCache();

        Assert.False(cache.TryStore(Key(Query("example.com")), Positive("example.com", ttl: 0)));
    }

    [Fact]
    public void WithTheCacheSwitchedOff_NothingIsStoredOrServed()
    {
        ForwardedResponseCache cache = CreateCache();
        DnsMessage request = Query("example.com");
        cache.TryStore(Key(request), Positive("example.com"));

        _forwardingOptions.EnableCache = false;

        Assert.Null(cache.TryGet(Key(request), request));
        Assert.False(cache.TryStore(Key(Query("other.example")), Positive("other.example")));

        _forwardingOptions.EnableCache = true;
        Assert.NotNull(cache.TryGet(Key(request), request));
        Assert.Null(cache.TryGet(Key(Query("other.example")), Query("other.example")));
    }

    // --- Serve-stale ---------------------------------------------------------------------------

    [Fact]
    public void Stale_IsServedWithAThirtySecondTtlOnlyWhenServeStaleIsOn()
    {
        ForwardedResponseCache cache = CreateCache();
        DnsMessage request = Query("example.com");
        cache.TryStore(Key(request), Positive("example.com", ttl: 60));

        _clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Null(cache.TryGet(Key(request), request));
        Assert.Null(cache.TryGetStale(Key(request), request));

        _recursiveOptions.EnableServeStale = true;
        _recursiveOptions.MaxStaleServeAge = TimeSpan.FromHours(1);

        DnsMessage stale = cache.TryGetStale(Key(request), request);
        Assert.Equal(ForwardedResponseCache.StaleAnswerTtlSeconds, stale.Answers[0].TimeToLive);

        _clock.Advance(TimeSpan.FromHours(1));
        Assert.Null(cache.TryGetStale(Key(request), request));
    }

    [Fact]
    public void Sweep_KeepsExpiredAnswersForTheServeStaleWindowOnly()
    {
        _recursiveOptions.EnableServeStale = true;
        _recursiveOptions.MaxStaleServeAge = TimeSpan.FromHours(1);
        ForwardedResponseCache cache = CreateCache();
        cache.TryStore(Key(Query("example.com")), Positive("example.com", ttl: 60));

        _clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(0, cache.EvictExpiredAndOverCapacity());

        _clock.Advance(TimeSpan.FromMinutes(31));
        Assert.Equal(1, cache.EvictExpiredAndOverCapacity());
    }

    // --- Size bound, flush, removal ------------------------------------------------------------

    [Fact]
    public void SizeBound_EvictsTheSoonestToExpireFirst()
    {
        _forwardingOptions.MaxCacheEntries = 2;
        ForwardedResponseCache cache = CreateCache();
        cache.TryStore(Key(Query("a.example")), Positive("a.example", ttl: 300));
        cache.TryStore(Key(Query("b.example")), Positive("b.example", ttl: 100));
        cache.TryStore(Key(Query("c.example")), Positive("c.example", ttl: 200));

        Assert.Equal(1, cache.EvictExpiredAndOverCapacity());

        Assert.Null(cache.TryGet(Key(Query("b.example")), Query("b.example")));
        Assert.NotNull(cache.TryGet(Key(Query("a.example")), Query("a.example")));
        Assert.NotNull(cache.TryGet(Key(Query("c.example")), Query("c.example")));

        DnsCacheEvictedEvent evicted = Assert.IsType<DnsCacheEvictedEvent>(
            Assert.Single(_publisher.Published)
        );
        Assert.Equal("forwarded-expired-or-capacity", evicted.Reason);
    }

    [Fact]
    public void Statistics_ReportEntriesHitsMissesAndTheBound()
    {
        ForwardedResponseCache cache = CreateCache();
        cache.TryStore(Key(Query("example.com")), Positive("example.com"));
        cache.TryGet(Key(Query("example.com")), Query("example.com"));
        cache.TryGet(Key(Query("example.org")), Query("example.org"));

        DnsCacheStatistics statistics = ((IDnsCache)cache).GetStatistics();
        Assert.Equal("forwarded", statistics.Name);
        Assert.Equal(1, statistics.EntryCount);
        Assert.Equal(100, statistics.MaxEntries);
        Assert.Equal(1, statistics.Hits);
        Assert.Equal(1, statistics.Misses);
    }

    [Fact]
    public void Flush_EmptiesTheCache()
    {
        ForwardedResponseCache cache = CreateCache();
        cache.TryStore(Key(Query("example.com")), Positive("example.com"));
        cache.TryStore(Key(Query("example.org")), Positive("example.org"));

        Assert.Equal(2, cache.Flush());
        Assert.Null(cache.TryGet(Key(Query("example.com")), Query("example.com")));
        Assert.Equal(0, cache.GetStatistics().EntryCount);
    }

    [Fact]
    public void Remove_ForgetsOneNameAcrossTypesAndUpstreamSets()
    {
        ForwardedResponseCache cache = CreateCache();
        DnsMessage a = Query("example.com");
        DnsMessage aaaa = Query("example.com", DnsResourceRecordTypeEnum.Aaaa);
        cache.TryStore(Key(a), Positive("example.com"));
        cache.TryStore(Key(a, upstreams: ["10.0.0.53"]), Positive("example.com"));
        cache.TryStore(
            Key(aaaa),
            Response(DnsResponseCodeEnum.NoError, authority: [Soa("example.com", 300, 300)])
        );
        cache.TryStore(Key(Query("www.example.com")), Positive("www.example.com"));

        Assert.Equal(3, cache.Remove(DnsName.Parse("Example.Com")));

        Assert.Null(cache.TryGet(Key(a), a));
        Assert.Null(cache.TryGet(Key(aaaa), aaaa));
        Assert.NotNull(cache.TryGet(Key(Query("www.example.com")), Query("www.example.com")));
    }

    // --- Per-client shape -------------------------------------------------------------------

    [Fact]
    public void Hit_ForASecureAnswerAndADoClient_SetsAdAndKeepsTheSignatures()
    {
        ForwardedResponseCache cache = CreateCache();
        DnsMessage request = Query("example.com", dnssecOk: true);
        cache.TryStore(Key(request), SignedUpstreamAnswer(), isSecure: true);

        DnsMessage hit = cache.TryGet(Key(request), request);

        Assert.True(hit.AuthenticData);
        Assert.Equal(2, hit.Answers.Count);
        Assert.Single(hit.AuthorityRecords);
        Assert.True(hit.Edns.DnssecOk);
        Assert.Empty(hit.Edns.Options);
    }

    [Fact]
    public void Hit_ForASecureAnswerAndAClientWithoutDo_DropsDnssecRecordsAndAd()
    {
        ForwardedResponseCache cache = CreateCache();
        DnsMessage request = Query("example.com", edns: false);
        cache.TryStore(Key(request), SignedUpstreamAnswer(), isSecure: true);

        DnsMessage hit = cache.TryGet(Key(request), request);

        Assert.False(hit.AuthenticData);
        Assert.Equal([DnsResourceRecordTypeEnum.A], hit.Answers.Select(record => record.Type));
        Assert.Empty(hit.AuthorityRecords);
        Assert.Null(hit.Edns);
    }

    [Fact]
    public void Hit_ForAnAnswerThatWasNotSecure_NeverSetsAdEvenWhenTheUpstreamDid()
    {
        ForwardedResponseCache cache = CreateCache();
        DnsMessage request = Query("example.com", dnssecOk: true);
        cache.TryStore(Key(request), SignedUpstreamAnswer(), isSecure: false);

        Assert.False(cache.TryGet(Key(request), request).AuthenticData);
    }

    [Fact]
    public void Hit_ForAClientThatOnlySetsAd_KeepsAdOnASecureAnswer()
    {
        ForwardedResponseCache cache = CreateCache();
        DnsMessage request = Query("example.com", edns: false);
        request.AuthenticData = true;
        cache.TryStore(Key(request), SignedUpstreamAnswer(), isSecure: true);

        Assert.True(cache.TryGet(Key(request), request).AuthenticData);
    }

    [Fact]
    public void Hit_ForAQueryForTheSignatureType_KeepsTheSignaturesWithoutDo()
    {
        ForwardedResponseCache cache = CreateCache();
        DnsMessage request = Query("example.com", DnsResourceRecordTypeEnum.RrSig, edns: false);
        DnsMessage upstream = Response(
            DnsResponseCodeEnum.NoError,
            [Signature("example.com", DnsResourceRecordTypeEnum.A)]
        );
        cache.TryStore(Key(request), upstream);

        DnsMessage hit = cache.TryGet(Key(request), request);

        Assert.Single(hit.Answers);
    }

    [Fact]
    public void Stale_ForASecureAnswer_NeverSetsAd()
    {
        _recursiveOptions.EnableServeStale = true;
        _recursiveOptions.MaxStaleServeAge = TimeSpan.FromHours(1);
        ForwardedResponseCache cache = CreateCache();
        DnsMessage request = Query("example.com", dnssecOk: true);
        cache.TryStore(Key(request), SignedUpstreamAnswer(), isSecure: true);
        _clock.Advance(TimeSpan.FromMinutes(10));

        DnsMessage stale = cache.TryGetStale(Key(request), request);

        Assert.NotNull(stale);
        Assert.False(stale.AuthenticData);
        Assert.Equal(2, stale.Answers.Count);
    }

    /// <summary>
    ///     This method builds an RRSIG over an A record with the given Original TTL and
    ///     expiration, for the signature-lifetime cap tests.
    /// </summary>
    private static DnsResourceRecord SignatureLasting(
        string name,
        uint originalTtl,
        DateTimeOffset expiration
    ) =>
        new(
            DnsName.Parse(name),
            DnsResourceRecordTypeEnum.RrSig,
            DnsClassEnum.Internet,
            3600, // The record TTL stays high so that only the RRSIG fields can shorten the entry.
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.RrSig,
                new RrSigRecordData(
                    DnsResourceRecordTypeEnum.A,
                    DnssecAlgorithmEnum.Ed25519,
                    2,
                    originalTtl,
                    (uint)expiration.ToUnixTimeSeconds(),
                    (uint)expiration.AddDays(-30).ToUnixTimeSeconds(),
                    1234,
                    DnsName.Parse("example.com"),
                    new byte[64]
                )
            )
        );

    [Fact]
    public void TryStore_WithASignatureExpiringBeforeTheTtl_ExpiresWithTheSignature()
    {
        ForwardedResponseCache cache = CreateCache();
        DnsMessage request = Query("example.com", dnssecOk: true);
        DnsMessage answer = Response(
            DnsResponseCodeEnum.NoError,
            [
                A("example.com", 3600),
                SignatureLasting("example.com", 3600, _clock.GetUtcNow().AddSeconds(60)),
            ]
        );
        cache.TryStore(Key(request), answer, isSecure: true);

        _clock.Advance(TimeSpan.FromSeconds(59));
        Assert.NotNull(cache.TryGet(Key(request), request));

        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Null(cache.TryGet(Key(request), request));
    }

    [Fact]
    public void TryStore_WithASignatureOriginalTtlBelowTheRecordTtl_ExpiresAtTheOriginalTtl()
    {
        ForwardedResponseCache cache = CreateCache();
        DnsMessage request = Query("example.com", dnssecOk: true);
        DnsMessage answer = Response(
            DnsResponseCodeEnum.NoError,
            [
                A("example.com", 3600),
                SignatureLasting("example.com", 60, _clock.GetUtcNow().AddDays(30)),
            ]
        );
        cache.TryStore(Key(request), answer, isSecure: true);

        _clock.Advance(TimeSpan.FromSeconds(59));
        Assert.NotNull(cache.TryGet(Key(request), request));

        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Null(cache.TryGet(Key(request), request));
    }

    [Fact]
    public void TryStore_ANegativeAnswerWithASignatureExpiringSoon_ExpiresWithTheSignature()
    {
        ForwardedResponseCache cache = CreateCache();
        DnsMessage request = Query("missing.example.com", dnssecOk: true);
        DnsMessage answer = Response(
            DnsResponseCodeEnum.NxDomain,
            [],
            [
                Soa("example.com", 3600, 3600),
                SignatureLasting("example.com", 3600, _clock.GetUtcNow().AddSeconds(60)),
            ]
        );
        cache.TryStore(Key(request), answer, isSecure: true);

        _clock.Advance(TimeSpan.FromSeconds(61));

        Assert.Null(cache.TryGet(Key(request), request));
    }

    [Fact]
    public void Hit_TwoHitsForTheSameEntry_ShareNoMessageState()
    {
        ForwardedResponseCache cache = CreateCache();
        DnsMessage request = Query("example.com", dnssecOk: true);
        cache.TryStore(Key(request), SignedUpstreamAnswer(), isSecure: true);

        DnsMessage first = cache.TryGet(Key(request), request);
        first.Answers.Clear();
        first.AuthenticData = false;
        first.Edns = null;

        DnsMessage second = cache.TryGet(Key(request), request);

        Assert.Equal(2, second.Answers.Count);
        Assert.True(second.AuthenticData);
        Assert.NotNull(second.Edns);
    }
}
