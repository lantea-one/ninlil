using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Core.Caching;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Recursive;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Caching;

/// <summary>
///     This class caches whole forwarded answers, so a repeat of the same question is answered
///     locally until the answer's TTL runs out. <see cref="ForwarderQueryResolver" /> uses it
///     through <see cref="IForwardedResponseCache" />, and the cache endpoint and
///     <see cref="DnsCacheEvictionService" /> through <see cref="IDnsCache" />. It stores through
///     the same <see cref="DnsCacheStore{TKey, TEntry}" /> as the recursive cache, so expiry, the
///     size bound, serve-stale retention, eviction events, flushing and per-name removal behave
///     the same for both.
///     <para>
///         <b>Why a response cache and not the recursive RRset cache.</b> The recursive cache
///         holds single RRsets that the iterative engine assembles into answers. A forwarder hands
///         back a finished message (a CNAME chain, its RRSIGs when DO was set, an SOA and NSEC
///         proof for a negative answer) whose content depends on the DO and CD bits,
///         on which forwarder set answered and on the ECS subnet sent. Splitting that into
///         RRsets would lose the DNSSEC and negative-answer framing, and would let a
///         conditional forwarder's answer leak into the global view, so the cache keeps the
///         whole message under a key that captures all of those inputs
///         (<see cref="ForwardedResponseCacheKey" />).
///     </para>
///     <para>
///         <b>What is never cached.</b> Only the upstream's answer is stored, before anything
///         client-specific happens to it. The pipeline applies response filtering, CNAME-cloaking
///         checks and cookies to whatever the dispatcher returns, on a cache hit as on a fresh
///         answer, so every client still gets its own block lists. A
///         truncated or TSIG-signed answer, SERVFAIL, REFUSED and every other error code, a
///         negative answer without an SOA to bound it, and a TTL of zero are not cached (see
///         <see cref="ComputeLifetime" />). A forwarder that failed or answered with bogus DNSSEC
///         never reaches this cache at all.
///     </para>
/// </summary>
public sealed class ForwardedResponseCache : IForwardedResponseCache, IDnsCache
{
    /// <summary>
    ///     This constant contains this cache's <see cref="IDnsCache.Name" />.
    /// </summary>
    public const string CacheName = "forwarded";

    /// <summary>
    ///     This field contains the longest a positive answer is cached, however long its TTL:
    ///     seven days, the cap RFC 8767 §4 recommends, so a misconfigured multi-year TTL cannot
    ///     pin an answer in memory indefinitely.
    /// </summary>
    public static readonly TimeSpan MaxPositiveLifetime = TimeSpan.FromDays(7);

    /// <summary>
    ///     This field contains the longest a negative (NXDOMAIN or NODATA) answer is cached:
    ///     three hours, the upper end of what RFC 2308 §5 recommends, so a name that is created
    ///     after a lookup failed becomes visible within a bounded time.
    /// </summary>
    public static readonly TimeSpan MaxNegativeLifetime = TimeSpan.FromHours(3);

    /// <summary>
    ///     This constant contains the TTL given to every record of a stale answer: thirty
    ///     seconds, the value RFC 8767 §4 recommends.
    /// </summary>
    public const uint StaleAnswerTtlSeconds = 30;

    private readonly IOptionsMonitor<ForwardingOptions> _forwardingOptions;
    private readonly IOptionsMonitor<RecursiveServerOptions> _recursiveOptions;
    private readonly DnsCacheStore<ForwardedResponseCacheKey, ForwardedResponseCacheEntry> _store;

    /// <summary>
    ///     This constructor builds an empty cache.
    /// </summary>
    /// <param name="forwardingOptions">
    ///     The live <see cref="ForwardingOptions.EnableCache" /> switch and
    ///     <see cref="ForwardingOptions.MaxCacheEntries" /> bound.
    /// </param>
    /// <param name="recursiveOptions">
    ///     The serve-stale switch and window (<see cref="RecursiveServerOptions.EnableServeStale" />,
    ///     <see cref="RecursiveServerOptions.MaxStaleServeAge" />). Serve-stale is one server-wide
    ///     policy, so forwarded answers follow the recursive setting.
    /// </param>
    /// <param name="eventPublisher">The publisher eviction events go out through.</param>
    /// <param name="timeProvider">The clock, replaceable in tests; the system clock by default.</param>
    public ForwardedResponseCache(
        IOptionsMonitor<ForwardingOptions> forwardingOptions,
        IOptionsMonitor<RecursiveServerOptions> recursiveOptions,
        IDnsServerEventPublisher eventPublisher,
        TimeProvider timeProvider = null
    )
    {
        _forwardingOptions = forwardingOptions;
        _recursiveOptions = recursiveOptions;
        _store = new DnsCacheStore<ForwardedResponseCacheKey, ForwardedResponseCacheEntry>(
            CacheName,
            key => key.Name,
            () => forwardingOptions.CurrentValue.MaxCacheEntries,
            () => StaleWindow,
            eventPublisher,
            "forwarded-expired-or-capacity",
            timeProvider
        );
    }

    /// <inheritdoc />
    public string Name => CacheName;

    /// <summary>
    ///     This property contains how long past expiry an answer may still be served stale, or
    ///     zero when serve-stale is off.
    /// </summary>
    private TimeSpan StaleWindow =>
        _recursiveOptions.CurrentValue.EnableServeStale
            ? _recursiveOptions.CurrentValue.MaxStaleServeAge
            : TimeSpan.Zero;

    /// <inheritdoc />
    public DnsMessage TryGet(ForwardedResponseCacheKey key, DnsMessage request)
    {
        if (!_forwardingOptions.CurrentValue.EnableCache)
            return null;

        return _store.TryGetFresh(key, out ForwardedResponseCacheEntry entry)
            ? entry.BuildResponse(request, _store.UtcNow)
            : null;
    }

    /// <inheritdoc />
    public DnsMessage TryGetStale(ForwardedResponseCacheKey key, DnsMessage request)
    {
        if (!_forwardingOptions.CurrentValue.EnableCache)
            return null;

        TimeSpan staleWindow = StaleWindow;

        if (staleWindow <= TimeSpan.Zero)
            return null;

        return _store.TryGetStale(key, staleWindow, out ForwardedResponseCacheEntry entry)
            ? entry.BuildStaleResponse(request, StaleAnswerTtlSeconds)
            : null;
    }

    /// <inheritdoc />
    public bool TryStore(ForwardedResponseCacheKey key, DnsMessage response)
    {
        if (!_forwardingOptions.CurrentValue.EnableCache)
            return false;

        DnsQuestion question = new(key.Name, key.Type, key.Class);

        if (ComputeLifetime(question, response) is not { } lifetime)
            return false;

        DateTimeOffset now = _store.UtcNow;
        _store.Set(key, new ForwardedResponseCacheEntry(response, now, now + lifetime));
        return true;
    }

    /// <summary>
    ///     This method decides whether an upstream answer may be cached at all and, if so, for
    ///     how long.
    ///     <list type="bullet">
    ///         <item>
    ///             A positive answer (NOERROR with a record of the queried type, or any record for
    ///             an ANY query) lives for the
    ///             lowest TTL of any record in it, capped at <see cref="MaxPositiveLifetime" />:
    ///             the whole message is served as one, so it must not outlive its shortest-lived
    ///             part.
    ///         </item>
    ///         <item>
    ///             A negative answer (NXDOMAIN, or NOERROR without a record of the queried type:
    ///             NODATA, possibly after a CNAME) lives per RFC 2308 §5 for the lesser of the SOA
    ///             MINIMUM field and the lowest TTL of any record in the message, capped at
    ///             <see cref="MaxNegativeLifetime" />. Without an SOA there is nothing to bound it
    ///             by, and RFC 2308 says not to cache it.
    ///         </item>
    ///         <item>
    ///             Anything else is not cached: SERVFAIL, REFUSED, FORMERR, NOTIMP and every other
    ///             code, a truncated answer (incomplete), a TSIG-signed one (signed for one
    ///             transaction only), or a lifetime of zero.
    ///         </item>
    ///     </list>
    /// </summary>
    /// <param name="question">The question the answer is for.</param>
    /// <param name="response">The upstream answer.</param>
    /// <returns>How long to cache it, or <see langword="null" /> when it must not be cached.</returns>
    public static TimeSpan? ComputeLifetime(DnsQuestion question, DnsMessage response)
    {
        if (response.IsTruncated || response.Tsig is not null)
            return null;

        if (
            response.ResponseCode
            is not (DnsResponseCodeEnum.NoError or DnsResponseCodeEnum.NxDomain)
        )
            return null;

        // The OPT pseudo-record's "TTL" field carries EDNS flags rather than a lifetime, so it
        // never counts, should a decoder ever leave it among the ordinary records.
        List<DnsResourceRecord> records =
        [
            .. response
                .Answers.Concat(response.AuthorityRecords)
                .Concat(response.AdditionalRecords)
                .Where(record => record.Type != DnsResourceRecordTypeEnum.Opt),
        ];

        bool answersQuestion =
            response.ResponseCode == DnsResponseCodeEnum.NoError
            && response.Answers.Any(record =>
                record.Type == question.Type || question.Type == DnsResourceRecordTypeEnum.Any
            );

        TimeSpan lifetime;

        if (answersQuestion)
        {
            lifetime = TimeSpan.FromSeconds(records.Min(record => record.TimeToLive));

            if (lifetime > MaxPositiveLifetime)
                lifetime = MaxPositiveLifetime;
        }
        else
        {
            DnsResourceRecord soa = response.AuthorityRecords.FirstOrDefault(record =>
                record.Type == DnsResourceRecordTypeEnum.Soa
            );

            if (
                soa is null
                || DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.Soa, soa.Data)
                    is not SoaRecordData soaData
            )
                return null;

            uint seconds = Math.Min(
                soaData.MinimumTtlSeconds,
                records.Min(record => record.TimeToLive)
            );
            lifetime = TimeSpan.FromSeconds(seconds);

            if (lifetime > MaxNegativeLifetime)
                lifetime = MaxNegativeLifetime;
        }

        return lifetime > TimeSpan.Zero ? lifetime : null;
    }

    /// <inheritdoc />
    public DnsCacheStatistics GetStatistics() => _store.GetStatistics();

    /// <inheritdoc />
    public int Flush() => _store.Flush();

    /// <inheritdoc />
    public int Remove(DnsName name) => _store.RemoveName(name);

    /// <inheritdoc />
    public int EvictExpiredAndOverCapacity() => _store.EvictExpiredAndOverCapacity();
}
