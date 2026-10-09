using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Caching;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Validation;

/// <summary>
///     This class remembers which zones <see cref="DnssecValidator" /> has already proven secure
///     (with their validated DNSKEY RRset) or insecure, so a later validation does not fetch and
///     verify the chain again. It is the reason a repeated query below a top-level domain stays
///     validated at the cost of one signature check.
///     <para>
///         The cache is keyed by zone and class and registered as the <see cref="IDnsCache" />
///         named <c>dnssec</c>, so the cache REST endpoint reports, flushes and clears single names
///         in it, and <see cref="DnsCacheEvictionService" /> sweeps it. Failed results (Bogus,
///         Indeterminate) are never stored. An entry expires at the earliest TTL or signature
///         expiration among the records that proved it, and not after its parent. Every entry was
///         validated from the same trust anchors; the validator passes a fingerprint of the
///         anchor set with each call, and the cache empties itself when the fingerprint changes
///         (an RFC 5011 rollover), so no entry outlives the keys it rests on.
///     </para>
/// </summary>
/// <param name="options">
///     Read live for the size bound: a tenth of <see cref="RecursiveServerOptions.MaxCacheEntries" />,
///     and at least <see cref="MinimumCapacity" />.
/// </param>
/// <param name="eventPublisher">The publisher eviction events go out through.</param>
/// <param name="timeProvider">The clock, replaceable in tests; the system clock by default.</param>
public sealed class DnssecTrustCache(
    IOptionsMonitor<RecursiveServerOptions> options,
    IDnsServerEventPublisher eventPublisher,
    TimeProvider timeProvider = null
) : IDnsCache
{
    /// <summary>
    ///     This constant contains this cache's <see cref="IDnsCache.Name" />.
    /// </summary>
    public const string CacheName = "dnssec";

    /// <summary>
    ///     This constant contains the smallest size bound, whatever the recursive cache allows.
    /// </summary>
    public const int MinimumCapacity = 1000;

    private readonly DnsCacheStore<(DnsName Zone, DnsClassEnum Class), ZoneTrust> _store = new(
        CacheName,
        key => key.Zone,
        () => Math.Max(MinimumCapacity, options.CurrentValue.MaxCacheEntries / 10),
        () => TimeSpan.Zero,
        eventPublisher,
        "expired-or-capacity",
        timeProvider
    );

    private readonly Lock _fingerprintLock = new();
    private string _anchorFingerprint;

    /// <inheritdoc />
    public string Name => CacheName;

    /// <summary>
    ///     This property contains the current UTC time on this cache's clock, so the validator
    ///     computes expiries with the clock they are checked against.
    /// </summary>
    public DateTimeOffset UtcNow => _store.UtcNow;

    /// <summary>
    ///     This method looks up a fresh result for a zone.
    /// </summary>
    /// <param name="zone">The zone apex.</param>
    /// <param name="recordClass">The class.</param>
    /// <param name="anchorFingerprint">The fingerprint of the trust anchors in use.</param>
    /// <returns>The cached result, or <see langword="null" />.</returns>
    public ZoneTrust TryGet(DnsName zone, DnsClassEnum recordClass, string anchorFingerprint)
    {
        Synchronize(anchorFingerprint);

        return _store.TryGetFresh((zone, recordClass), out ZoneTrust trust) ? trust : null;
    }

    /// <summary>
    ///     This method stores a secure or insecure result. Any other state is ignored.
    /// </summary>
    /// <param name="zone">The zone apex.</param>
    /// <param name="recordClass">The class.</param>
    /// <param name="trust">The result to keep.</param>
    /// <param name="anchorFingerprint">The fingerprint of the trust anchors in use.</param>
    public void Store(
        DnsName zone,
        DnsClassEnum recordClass,
        ZoneTrust trust,
        string anchorFingerprint
    )
    {
        if (
            trust.State
            is not (DnssecValidationStateEnum.Secure or DnssecValidationStateEnum.Insecure)
        )
            return;

        Synchronize(anchorFingerprint);
        _store.Set((zone, recordClass), trust);
    }

    /// <inheritdoc />
    public DnsCacheStatistics GetStatistics() => _store.GetStatistics();

    /// <inheritdoc />
    public int Flush() => _store.Flush();

    /// <inheritdoc />
    public int Remove(DnsName name) => _store.RemoveName(name);

    /// <inheritdoc />
    public int EvictExpiredAndOverCapacity() => _store.EvictExpiredAndOverCapacity();

    /// <summary>
    ///     This method empties the cache when the trust anchors differ from those the entries were
    ///     validated with.
    /// </summary>
    private void Synchronize(string anchorFingerprint)
    {
        bool changed;

        lock (_fingerprintLock)
        {
            changed = _anchorFingerprint is not null && _anchorFingerprint != anchorFingerprint;
            _anchorFingerprint = anchorFingerprint;
        }

        if (changed)
            _store.Flush();
    }
}
