using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.AccessControl;
using Ninlil.Dns.Server.Core.RateLimiting;

// Define our namespace.
namespace Ninlil.Dns.Server.RateLimiting;

/// <summary>
///     This class implements <see cref="IDnsRateLimiter" /> as a classic token bucket per
///     client IP, held entirely in memory -- by the user's own explicit choice, given this
///     project's current single-instance homelab deployment target, a bucket resetting on
///     restart is harmless and needs no persisted state or repository at all. Also implements
///     this interface's own second, independent layer -- a fixed-window counter bucketed by CIDR
///     subnet rather than exact IP -- entirely separately from the token-bucket state above; the
///     two layers share nothing but this class and <paramref name="options" />.
/// </summary>
/// <param name="options">This limiter's own configured rate, burst size, and tracking limits.</param>
/// <param name="accessControlEngine">
///     The real per-client/network access-control engine this limiter consults so a
///     client matching an enabled rule can be exempted from rate limiting entirely -- checked
///     before a bucket is ever looked up or created for that client, so an exempt client's own
///     bucket is never even touched. Defaults to <see langword="null" /> -- the same
///     optional-cross-cutting-dependency idiom <c>DnsQueryProcessingPipeline</c> already
///     establishes for its own filter/rewrite engines -- so every existing caller that constructs
///     this limiter directly keeps compiling unchanged; a <see langword="null" /> engine behaves
///     as if no rule ever grants an exemption, never as an error.
/// </param>
/// <param name="randomSource">
///     The source of randomness <see cref="CheckSubnetLimitAsync" /> uses to decide, per
///     over-limit UDP query, whether it lands on the <see cref="DnsSubnetRateLimitDecisionEnum.RespondTruncated" />
///     or <see cref="DnsSubnetRateLimitDecisionEnum.Drop" /> side of
///     <see cref="DnsRateLimitOptions.SubnetTruncatePercentage" />'s own split -- a function
///     returning a value in <c>[0, 1)</c>, matching <see cref="Random.NextDouble" />'s own
///     contract. Defaults to <see cref="Random.Shared" />'s own <see cref="Random.NextDouble" />;
///     overridable so a test can inject a deterministic sequence instead of asserting against
///     real randomness.
/// </param>
public sealed class TokenBucketDnsRateLimiter(
    IOptions<DnsRateLimitOptions> options,
    IDnsAccessControlEngine accessControlEngine = null,
    Func<double> randomSource = null
) : IDnsRateLimiter
{
    /// <summary>
    ///     This field contains the actual randomness source <see cref="CheckSubnetLimitAsync" />
    ///     consults, resolving this class's own <c>randomSource</c> constructor parameter's
    ///     <see langword="null" /> default to <see cref="Random.Shared" />'s
    ///     <see cref="Random.NextDouble" /> once, here, rather than re-checking for
    ///     <see langword="null" /> on every single call.
    /// </summary>
    private readonly Func<double> _randomSource =
        randomSource ?? (() => Random.Shared.NextDouble());

    /// <summary>
    ///     This property contains every client IP's own bucket currently being tracked, keyed by
    ///     source address. Exposed internally so <see cref="DnsRateLimitCleanupService" /> can
    ///     sweep it without this class needing to run its own background timer too.
    /// </summary>
    public ConcurrentDictionary<IPAddress, Bucket> Buckets { get; } = new();

    /// <summary>
    ///     This property contains every subnet's own fixed-window query counters currently being
    ///     tracked, keyed by that subnet's masked network address (see
    ///     <see cref="MaskToPrefix" />). Exposed internally for the same reason
    ///     <see cref="Buckets" /> is: so <see cref="DnsRateLimitCleanupService" /> can sweep it
    ///     without this class needing its own background timer too.
    /// </summary>
    public ConcurrentDictionary<IPAddress, SubnetWindow> SubnetWindows { get; } = new();

    /// <inheritdoc />
    public async ValueTask<bool> TryAcquireAsync(
        IPAddress remoteAddress,
        CancellationToken stoppingToken = default
    )
    {
        if (!options.Value.Enable)
            return true;

        // A client matching an enabled access-control rule granting RateLimitBypass is exempt
        // entirely -- checked here, before the bucket lookup/creation below, so an
        // exempt client's own bucket is never even touched. accessControlEngine defaults to null
        // (see this class's own constructor doc comment), in which case this check is skipped
        // entirely and every client is limited exactly as before this capability existed.
        if (
            accessControlEngine is not null
            && await accessControlEngine.HasCapabilityAsync(
                remoteAddress,
                DnsAccessControlCapabilityEnum.RateLimitBypass,
                stoppingToken
            )
        )
            return true;

        if (!Buckets.TryGetValue(remoteAddress, out Bucket bucket))
        {
            // A source address this limiter has never seen before is only added if there is
            // still room in the tracking table -- see DnsRateLimitOptions.MaxTrackedClients's
            // own doc comment for why exceeding it fails open rather than denying.
            if (Buckets.Count >= options.Value.MaxTrackedClients)
                return true;

            bucket = Buckets.GetOrAdd(remoteAddress, _ => new Bucket(options.Value.BurstSize));
        }

        // The classic token-bucket refill-then-spend sequence, done under this bucket's own lock
        // so two concurrent queries from the same client never race on the same balance: first,
        // top the bucket up by however many tokens should have accrued since it was last
        // refilled (elapsed real time times the configured steady-state rate), capped at the
        // bucket's own burst capacity so a long-idle client can never bank an unbounded reserve.
        // Then, if at least one whole token remains, spend exactly one for this query and allow
        // it through; otherwise deny without touching the balance further.
        lock (bucket)
        {
            long now = Stopwatch.GetTimestamp();
            double elapsedSeconds = Stopwatch
                .GetElapsedTime(bucket.LastRefillTimestamp, now)
                .TotalSeconds;
            bucket.LastRefillTimestamp = now;
            bucket.LastTouchedUtc = DateTimeOffset.UtcNow;

            bucket.Tokens = Math.Min(
                options.Value.BurstSize,
                bucket.Tokens + elapsedSeconds * options.Value.QueriesPerSecond
            );

            if (bucket.Tokens < 1)
                return false;

            bucket.Tokens -= 1;
            return true;
        }
    }

    /// <inheritdoc />
    public async ValueTask<DnsSubnetRateLimitDecisionEnum> CheckSubnetLimitAsync(
        IPAddress remoteAddress,
        bool isUdp,
        CancellationToken stoppingToken = default
    )
    {
        if (!options.Value.Enable)
            return DnsSubnetRateLimitDecisionEnum.Allow;

        if (
            accessControlEngine is not null
            && await accessControlEngine.HasCapabilityAsync(
                remoteAddress,
                DnsAccessControlCapabilityEnum.RateLimitBypass,
                stoppingToken
            )
        )
            return DnsSubnetRateLimitDecisionEnum.Allow;

        if (IsBypassed(remoteAddress, options.Value.SubnetBypassCidrs))
            return DnsSubnetRateLimitDecisionEnum.Allow;

        IPAddress subnetKey = MaskToPrefix(
            remoteAddress,
            options.Value.SubnetPrefixLengthIPv4,
            options.Value.SubnetPrefixLengthIPv6
        );

        if (
            !SubnetWindows.TryGetValue(subnetKey, out SubnetWindow window)
            && SubnetWindows.Count >= options.Value.MaxTrackedClients
        )
            // The same fail-open posture Buckets' own MaxTrackedClients check already takes,
            // applied to this second, independent dictionary -- a subnet-tracking table that
            // itself became unboundedly large must never become its own denial-of-service vector
            // either.
            return DnsSubnetRateLimitDecisionEnum.Allow;

        window = SubnetWindows.GetOrAdd(subnetKey, _ => new SubnetWindow(Stopwatch.GetTimestamp()));

        lock (window)
        {
            long now = Stopwatch.GetTimestamp();

            // A fixed (not sliding) window: once SubnetSampleWindow has elapsed since it was
            // last reset, both counters snap back to zero and a fresh window starts from now --
            // simpler than a sliding window and adequate for catching a genuine sustained flood,
            // which this check exists for, rather than smoothing every short-lived spike.
            if (
                Stopwatch.GetElapsedTime(window.WindowStartTimestamp, now).TotalSeconds
                >= options.Value.SubnetSampleWindow.TotalSeconds
            )
            {
                window.WindowStartTimestamp = now;
                window.UdpCount = 0;
                window.TcpCount = 0;
            }

            window.LastTouchedUtc = DateTimeOffset.UtcNow;

            if (isUdp)
            {
                window.UdpCount++;

                if (window.UdpCount <= options.Value.SubnetUdpQueryLimit)
                    return DnsSubnetRateLimitDecisionEnum.Allow;

                // Past the limit, UDP gets a graduated response rather than a flat drop: a
                // random draw against SubnetTruncatePercentage decides whether this particular
                // over-limit query is answered truncated (pushing a legitimate retry onto TCP,
                // where the per-subnet TCP limit below takes over) or dropped outright -- see
                // this limiter's own constructor doc comment for why a random split, rather than
                // e.g. every Nth query, is used.
                return _randomSource() < options.Value.SubnetTruncatePercentage / 100.0
                    ? DnsSubnetRateLimitDecisionEnum.RespondTruncated
                    : DnsSubnetRateLimitDecisionEnum.Drop;
            }

            // TCP gets no graduated response -- an over-limit TCP query is simply dropped, since
            // there is no lower-cost transport left to redirect a legitimate retry onto.
            window.TcpCount++;

            return window.TcpCount <= options.Value.SubnetTcpQueryLimit
                ? DnsSubnetRateLimitDecisionEnum.Allow
                : DnsSubnetRateLimitDecisionEnum.Drop;
        }
    }

    /// <summary>
    ///     This method checks whether <paramref name="address" /> falls within any CIDR entry in
    ///     <paramref name="bypassCidrs" />. A malformed entry is skipped rather than throwing --
    ///     the same "an operator's configuration mistake degrades a feature, it does not crash the
    ///     server" posture this project applies to comparable operator-supplied text elsewhere.
    /// </summary>
    private static bool IsBypassed(IPAddress address, List<string> bypassCidrs)
    {
        foreach (string entry in bypassCidrs)
        {
            int slashIndex = entry.IndexOf('/');

            if (slashIndex < 0)
                continue;

            if (
                !IPAddress.TryParse(entry.AsSpan(0, slashIndex), out IPAddress network)
                || !byte.TryParse(entry.AsSpan(slashIndex + 1), out byte prefixLength)
            )
                continue;

            if (network.AddressFamily != address.AddressFamily)
                continue;

            if (Equals(MaskToPrefix(address, prefixLength, prefixLength), network))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     This method masks <paramref name="address" /> down to its own network portion --
    ///     <paramref name="prefixLengthIPv4" /> or <paramref name="prefixLengthIPv6" /> leading
    ///     bits kept, every bit beyond that zeroed -- which is what actually turns "one address"
    ///     into "one subnet bucket key": every address sharing the same masked result shares the
    ///     same bucket.
    /// </summary>
    private static IPAddress MaskToPrefix(
        IPAddress address,
        byte prefixLengthIPv4,
        byte prefixLengthIPv6
    )
    {
        byte[] addressBytes = address.GetAddressBytes();
        byte prefixLength =
            address.AddressFamily == AddressFamily.InterNetworkV6
                ? prefixLengthIPv6
                : prefixLengthIPv4;
        int fullBytes = prefixLength / 8;
        int remainderBits = prefixLength % 8;

        for (int i = 0; i < addressBytes.Length; i++)
            if (i < fullBytes)
                continue;
            else if (i == fullBytes && remainderBits > 0)
                addressBytes[i] &= (byte)(0xFF << (8 - remainderBits));
            else
                addressBytes[i] = 0;

        return new IPAddress(addressBytes);
    }

    /// <summary>
    ///     This class represents a single subnet's own fixed-window query counters. Every access
    ///     to its mutable fields must hold a lock on the instance itself -- the same instance
    ///     <see cref="CheckSubnetLimitAsync" /> already locks around a window-check-and-count.
    /// </summary>
    public sealed class SubnetWindow(long windowStartTimestamp)
    {
        /// <summary>
        ///     This field contains the <see cref="Stopwatch" /> timestamp this window last reset
        ///     at.
        /// </summary>
        public long WindowStartTimestamp = windowStartTimestamp;

        /// <summary>This field contains how many UDP queries this window has counted so far.</summary>
        public int UdpCount;

        /// <summary>This field contains how many TCP queries this window has counted so far.</summary>
        public int TcpCount;

        /// <summary>
        ///     This field contains when this window was last touched by a real query, consulted
        ///     by <see cref="DnsRateLimitCleanupService" /> to decide whether this subnet bucket
        ///     is stale enough to evict.
        /// </summary>
        public DateTimeOffset LastTouchedUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    ///     This class represents a single client IP's own token bucket. Every access to its
    ///     mutable fields must hold a lock on the instance itself -- the same instance
    ///     <see cref="TryAcquireAsync" /> already locks around a refill-and-spend check.
    /// </summary>
    public sealed class Bucket(double initialTokens)
    {
        /// <summary>
        ///     This field contains the number of tokens currently available to spend -- one per
        ///     query allowed through, refilled continuously up to <see cref="DnsRateLimitOptions.BurstSize" />.
        /// </summary>
        public double Tokens = initialTokens;

        /// <summary>
        ///     This field contains the <see cref="Stopwatch" /> timestamp this bucket was last
        ///     refilled at, used to compute how many tokens have accrued since.
        /// </summary>
        public long LastRefillTimestamp = Stopwatch.GetTimestamp();

        /// <summary>
        ///     This field contains when this bucket was last touched by a real query, consulted
        ///     by <see cref="DnsRateLimitCleanupService" /> to decide whether this bucket is
        ///     stale enough to evict.
        /// </summary>
        public DateTimeOffset LastTouchedUtc = DateTimeOffset.UtcNow;
    }
}
