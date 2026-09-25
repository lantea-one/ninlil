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
///     This class implements <see cref="IDnsRateLimiter" />, which
///     <c>DnsQueryProcessingPipeline</c> calls before decoding each query (see that class). It
///     has two independent layers that share only the options:
///     <list type="bullet">
///         <item>
///             A token bucket per client IP (<see cref="TryAcquireAsync" />): each query spends
///             one token, and tokens refill at <see cref="DnsRateLimitOptions.QueriesPerSecond" />
///             up to <see cref="DnsRateLimitOptions.BurstSize" />.
///         </item>
///         <item>
///             A fixed-window counter per subnet (<see cref="CheckSubnetLimitAsync" />), which
///             catches a flood spread over many addresses in one network.
///         </item>
///     </list>
///     All state is in memory and resets on restart, which is harmless for a limiter. A client
///     granted <see cref="DnsAccessControlCapabilityEnum.RateLimitBypass" /> by an access-control
///     rule skips both layers. <see cref="DnsRateLimitCleanupService" /> evicts idle entries.
/// </summary>
/// <param name="options">The rate, burst size, subnet limits and tracking limits.</param>
/// <param name="accessControlEngine">
///     The access-control engine consulted for the rate-limit bypass capability, before any
///     bucket or window is looked up or created, so an exempt client never gets one. It is
///     optional: with <see langword="null" /> no client is exempt.
/// </param>
/// <param name="randomSource">
///     A function returning a value in <c>[0, 1)</c>, like <see cref="Random.NextDouble" />, that
///     <see cref="CheckSubnetLimitAsync" /> uses to decide whether an over-limit UDP query gets
///     <see cref="DnsSubnetRateLimitDecisionEnum.RespondTruncated" /> or
///     <see cref="DnsSubnetRateLimitDecisionEnum.Drop" /> (see
///     <see cref="DnsRateLimitOptions.SubnetTruncatePercentage" />). It defaults to
///     <see cref="Random.Shared" />; tests pass a fixed sequence.
/// </param>
public sealed class TokenBucketDnsRateLimiter(
    IOptions<DnsRateLimitOptions> options,
    IDnsAccessControlEngine accessControlEngine = null,
    Func<double> randomSource = null
) : IDnsRateLimiter
{
    /// <summary>
    ///     This field contains the randomness source <see cref="CheckSubnetLimitAsync" /> uses:
    ///     the <c>randomSource</c> constructor parameter, or <see cref="Random.Shared" /> when it
    ///     is <see langword="null" />.
    /// </summary>
    private readonly Func<double> _randomSource =
        randomSource ?? (() => Random.Shared.NextDouble());

    /// <summary>
    ///     This property contains the bucket of every tracked client IP, keyed by source address.
    ///     It is public so <see cref="DnsRateLimitCleanupService" /> can sweep it and tests can
    ///     inspect it.
    /// </summary>
    public ConcurrentDictionary<IPAddress, Bucket> Buckets { get; } = new();

    /// <summary>
    ///     This property contains the window of every tracked subnet, keyed by the masked network
    ///     address (see <see cref="MaskToPrefix" />). It is public for the same reason as
    ///     <see cref="Buckets" />.
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

        // A client granted RateLimitBypass is exempt. The check runs before the bucket lookup
        // so an exempt client never gets a bucket.
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
            // A new address is only tracked while there is room; see
            // DnsRateLimitOptions.MaxTrackedClients for why a full table fails open.
            if (Buckets.Count >= options.Value.MaxTrackedClients)
                return true;

            bucket = Buckets.GetOrAdd(remoteAddress, _ => new Bucket(options.Value.BurstSize));
        }

        // Refill, then spend, under the bucket's lock so concurrent queries from one client do
        // not race on the balance. The refill adds elapsed time times QueriesPerSecond, capped at
        // BurstSize so an idle client cannot bank an unbounded reserve. One whole token lets the
        // query through; otherwise it is denied.
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
            // A full subnet table fails open, as the per-IP table does (see
            // DnsRateLimitOptions.MaxTrackedClients).
            return DnsSubnetRateLimitDecisionEnum.Allow;

        window = SubnetWindows.GetOrAdd(subnetKey, _ => new SubnetWindow(Stopwatch.GetTimestamp()));

        lock (window)
        {
            long now = Stopwatch.GetTimestamp();

            // A fixed, not sliding, window: once SubnetSampleWindow has passed since the last
            // reset, both counters go back to zero. It is simpler than a sliding window and good
            // enough for catching a sustained flood.
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

                // Past the limit, a random draw against SubnetTruncatePercentage decides whether
                // this query is answered truncated (a legitimate client retries over TCP, where
                // the TCP limit below applies) or dropped.
                return _randomSource() < options.Value.SubnetTruncatePercentage / 100.0
                    ? DnsSubnetRateLimitDecisionEnum.RespondTruncated
                    : DnsSubnetRateLimitDecisionEnum.Drop;
            }

            // An over-limit TCP query is dropped: there is no other transport to send the client
            // to.
            window.TcpCount++;

            return window.TcpCount <= options.Value.SubnetTcpQueryLimit
                ? DnsSubnetRateLimitDecisionEnum.Allow
                : DnsSubnetRateLimitDecisionEnum.Drop;
        }
    }

    /// <summary>
    ///     This method checks whether <paramref name="address" /> falls within any CIDR entry in
    ///     <paramref name="bypassCidrs" />. A malformed entry is skipped rather than throwing, so a
    ///     configuration mistake disables one entry instead of breaking query handling. An entry
    ///     must name the network address itself: host bits after the prefix make it match
    ///     nothing.
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
    ///     This method masks <paramref name="address" /> to its network portion, keeping
    ///     <paramref name="prefixLengthIPv4" /> or <paramref name="prefixLengthIPv6" /> leading
    ///     bits and zeroing the rest. The result is the subnet window key: addresses with the same
    ///     masked value share a window. An IPv4-mapped IPv6 address is treated as IPv6.
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
    ///     This class holds one subnet's fixed-window query counters. Every access to its fields
    ///     must hold a lock on the instance, as <see cref="CheckSubnetLimitAsync" /> and
    ///     <see cref="DnsRateLimitCleanupService" /> do.
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
        ///     This field contains when a query last touched this window.
        ///     <see cref="DnsRateLimitCleanupService" /> uses it to decide when to evict it.
        /// </summary>
        public DateTimeOffset LastTouchedUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    ///     This class holds one client IP's token bucket. Every access to its fields must hold a
    ///     lock on the instance, as <see cref="TryAcquireAsync" /> and
    ///     <see cref="DnsRateLimitCleanupService" /> do.
    /// </summary>
    public sealed class Bucket(double initialTokens)
    {
        /// <summary>
        ///     This field contains the tokens available to spend, one per query let through. It
        ///     refills up to <see cref="DnsRateLimitOptions.BurstSize" />.
        /// </summary>
        public double Tokens = initialTokens;

        /// <summary>
        ///     This field contains the <see cref="Stopwatch" /> timestamp this bucket was last
        ///     refilled at, used to compute how many tokens have accrued since.
        /// </summary>
        public long LastRefillTimestamp = Stopwatch.GetTimestamp();

        /// <summary>
        ///     This field contains when a query last touched this bucket.
        ///     <see cref="DnsRateLimitCleanupService" /> uses it to decide when to evict it.
        /// </summary>
        public DateTimeOffset LastTouchedUtc = DateTimeOffset.UtcNow;
    }
}
