using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Net;
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

    /// <summary>
    ///     This field counts the entries of <see cref="Buckets" /> the limiter added and the
    ///     cleanup removed through <see cref="TryRemoveBucket" />. It stands in for
    ///     <c>ConcurrentDictionary.Count</c>, which takes every bucket lock, on the path where
    ///     every query from a new address would otherwise pay for that. A slot is reserved before
    ///     the entry is added, so concurrent first queries cannot push the table past
    ///     <see cref="DnsRateLimitOptions.MaxTrackedClients" />. Entries put into the public
    ///     dictionary by other code (tests) are not counted.
    /// </summary>
    private int _trackedClientCount;

    /// <summary>
    ///     This field counts the entries of <see cref="SubnetWindows" /> the same way
    ///     <see cref="_trackedClientCount" /> counts the buckets.
    /// </summary>
    private int _trackedSubnetCount;

    /// <summary>
    ///     This field is the parsed form of <see cref="DnsRateLimitOptions.SubnetBypassCidrs" />,
    ///     built the first time a query needs it and again if the options hold a different list
    ///     (a new options instance). It is replaced as a whole, never changed in place, so a
    ///     concurrent query reads one consistent snapshot.
    /// </summary>
    private volatile BypassSnapshot _bypass;

    /// <summary>
    ///     This property is the number of tracked client buckets the limiter counts. It is what
    ///     the cap on <see cref="DnsRateLimitOptions.MaxTrackedClients" /> compares against.
    /// </summary>
    public int TrackedClientCount => Volatile.Read(ref _trackedClientCount);

    /// <summary>
    ///     This property is the number of tracked subnet windows the limiter counts, compared
    ///     against <see cref="DnsRateLimitOptions.MaxTrackedClients" /> in the same way.
    /// </summary>
    public int TrackedSubnetCount => Volatile.Read(ref _trackedSubnetCount);

    /// <summary>
    ///     This method removes one client bucket and keeps the count in step.
    ///     <see cref="DnsRateLimitCleanupService" /> evicts idle buckets through it.
    /// </summary>
    /// <param name="address">The client address the bucket is keyed by.</param>
    /// <returns><see langword="true" /> when a bucket was removed.</returns>
    public bool TryRemoveBucket(IPAddress address)
    {
        if (!Buckets.TryRemove(address, out _))
            return false;

        ReleaseSlot(ref _trackedClientCount);
        return true;
    }

    /// <summary>
    ///     This method removes one subnet window and keeps the count in step.
    ///     <see cref="DnsRateLimitCleanupService" /> evicts idle windows through it.
    /// </summary>
    /// <param name="subnetKey">The masked network address the window is keyed by.</param>
    /// <returns><see langword="true" /> when a window was removed.</returns>
    public bool TryRemoveSubnetWindow(IPAddress subnetKey)
    {
        if (!SubnetWindows.TryRemove(subnetKey, out _))
            return false;

        ReleaseSlot(ref _trackedSubnetCount);
        return true;
    }

    /// <summary>
    ///     This method gives one reserved slot back, never taking a counter below zero (entries
    ///     put into the public dictionaries directly were never counted).
    /// </summary>
    private static void ReleaseSlot(ref int counter)
    {
        int current = Volatile.Read(ref counter);

        while (current > 0)
        {
            int seen = Interlocked.CompareExchange(ref counter, current - 1, current);

            if (seen == current)
                return;

            current = seen;
        }
    }

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
            // DnsRateLimitOptions.MaxTrackedClients for why a full table fails open. The slot is
            // reserved first, so the table never passes the cap.
            if (Interlocked.Increment(ref _trackedClientCount) > options.Value.MaxTrackedClients)
            {
                Interlocked.Decrement(ref _trackedClientCount);
                return true;
            }

            Bucket created = new(options.Value.BurstSize);
            bucket = Buckets.GetOrAdd(remoteAddress, created);

            // Another query from the same address added its bucket first: give the slot back.
            if (!ReferenceEquals(bucket, created))
                Interlocked.Decrement(ref _trackedClientCount);
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

        if (IsBypassed(remoteAddress, GetBypass(options.Value.SubnetBypassCidrs)))
            return DnsSubnetRateLimitDecisionEnum.Allow;

        IPAddress subnetKey = MaskToPrefix(
            remoteAddress,
            options.Value.SubnetPrefixLengthIPv4,
            options.Value.SubnetPrefixLengthIPv6
        );

        if (!SubnetWindows.TryGetValue(subnetKey, out SubnetWindow window))
        {
            // A full subnet table fails open, as the per-IP table does (see
            // DnsRateLimitOptions.MaxTrackedClients). The slot is reserved before the add.
            if (Interlocked.Increment(ref _trackedSubnetCount) > options.Value.MaxTrackedClients)
            {
                Interlocked.Decrement(ref _trackedSubnetCount);
                return DnsSubnetRateLimitDecisionEnum.Allow;
            }

            SubnetWindow created = new(Stopwatch.GetTimestamp());
            window = SubnetWindows.GetOrAdd(subnetKey, created);

            if (!ReferenceEquals(window, created))
                Interlocked.Decrement(ref _trackedSubnetCount);
        }

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
    ///     This method returns the parsed bypass entries for the configured list, parsing them
    ///     only when the list is not the one already parsed. A malformed entry is skipped rather
    ///     than throwing, so a configuration mistake disables one entry instead of breaking query
    ///     handling. An entry must name the network address itself: host bits after the prefix
    ///     make it match nothing.
    /// </summary>
    private BypassSnapshot GetBypass(List<string> bypassCidrs)
    {
        BypassSnapshot snapshot = _bypass;

        // The options list is bound at startup and changes only with a restart, so the list
        // reference identifies one parse. Two threads may parse it at once; the results are equal.
        if (snapshot != null && ReferenceEquals(snapshot.Source, bypassCidrs))
            return snapshot;

        List<BypassNetwork> networks = [];

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

            // An entry written as an IPv4-mapped network (::ffff:192.0.2.0/120) names the same
            // hosts as the IPv4 one (/24): drop the 96 mapped bits from the prefix.
            if (network.IsIPv4MappedToIPv6 && prefixLength >= 96)
                prefixLength -= 96;

            network = ClientAddress.Normalize(network);

            networks.Add(new BypassNetwork(network.GetAddressBytes(), prefixLength));
        }

        snapshot = new BypassSnapshot(bypassCidrs, [.. networks]);
        _bypass = snapshot;
        return snapshot;
    }

    /// <summary>
    ///     This method checks whether <paramref name="address" /> falls within any parsed bypass
    ///     entry: the address masked to the entry's prefix must equal the entry's network address.
    ///     An entry of the other address family never matches.
    /// </summary>
    private static bool IsBypassed(IPAddress address, BypassSnapshot bypass)
    {
        if (bypass.Networks.Length == 0)
            return false;

        Span<byte> addressBytes = stackalloc byte[16];

        if (!address.TryWriteBytes(addressBytes, out int length))
            return false;

        Span<byte> masked = stackalloc byte[16];

        foreach (BypassNetwork network in bypass.Networks)
        {
            if (network.Bytes.Length != length)
                continue;

            MaskBytes(addressBytes[..length], masked[..length], network.PrefixLength);

            if (masked[..length].SequenceEqual(network.Bytes))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     This method masks <paramref name="address" /> to its network portion, keeping
    ///     <paramref name="prefixLengthIPv4" /> or <paramref name="prefixLengthIPv6" /> leading
    ///     bits and zeroing the rest. The result is the subnet window key: addresses with the same
    ///     masked value share a window. The pipeline passes normalized addresses, so an IPv4 client on a
    ///     dual-stack socket is masked as IPv4. Only the key's own address object is allocated.
    /// </summary>
    private static IPAddress MaskToPrefix(
        IPAddress address,
        byte prefixLengthIPv4,
        byte prefixLengthIPv6
    )
    {
        Span<byte> addressBytes = stackalloc byte[16];
        address.TryWriteBytes(addressBytes, out int length);
        byte prefixLength =
            address.AddressFamily == AddressFamily.InterNetworkV6
                ? prefixLengthIPv6
                : prefixLengthIPv4;

        MaskBytes(addressBytes[..length], addressBytes[..length], prefixLength);

        return new IPAddress(addressBytes[..length]);
    }

    /// <summary>
    ///     This method copies <paramref name="source" /> into <paramref name="destination" />
    ///     keeping the first <paramref name="prefixLength" /> bits and zeroing the rest. A prefix
    ///     longer than the address keeps every bit. The two spans may be the same span.
    /// </summary>
    private static void MaskBytes(
        ReadOnlySpan<byte> source,
        Span<byte> destination,
        byte prefixLength
    )
    {
        int fullBytes = prefixLength / 8;
        int remainderBits = prefixLength % 8;

        for (int i = 0; i < source.Length; i++)
            if (i < fullBytes)
                destination[i] = source[i];
            else if (i == fullBytes && remainderBits > 0)
                destination[i] = (byte)(source[i] & (byte)(0xFF << (8 - remainderBits)));
            else
                destination[i] = 0;
    }

    /// <summary>
    ///     This record is the bypass list as it was parsed, with the list it came from so a
    ///     different list triggers a new parse.
    /// </summary>
    private sealed record BypassSnapshot(List<string> Source, BypassNetwork[] Networks);

    /// <summary>
    ///     This record is one valid bypass entry: the network address bytes (IPv4 or IPv6) and the
    ///     prefix length to mask a client address to before comparing.
    /// </summary>
    private sealed record BypassNetwork(byte[] Bytes, byte PrefixLength);

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
