using System.Net;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.AccessControl;
using Ninlil.Dns.Server.Core.RateLimiting;
using Ninlil.Dns.Server.RateLimiting.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.RateLimiting.Tests;

/// <summary>
///     This class tests the per-IP token bucket in <see cref="TokenBucketDnsRateLimiter" />
///     (<see cref="TokenBucketDnsRateLimiter.TryAcquireAsync" />): burst, refill, per-client
///     independence, failing open when the table is full, cleanup of idle buckets and the
///     access-control bypass. It constructs the limiter directly, with a fake access-control
///     engine where needed.
/// </summary>
public class TokenBucketDnsRateLimiterTests
{
    private static readonly IPAddress ClientA = IPAddress.Parse("203.0.113.10");
    private static readonly IPAddress ClientB = IPAddress.Parse("203.0.113.20");

    private static TokenBucketDnsRateLimiter CreateLimiter(
        DnsRateLimitOptions options,
        IDnsAccessControlEngine accessControlEngine = null,
        Func<double> randomSource = null
    ) => new(Options.Create(options), accessControlEngine, randomSource);

    [Fact]
    public async Task DisabledLimiterAlwaysAllows()
    {
        TokenBucketDnsRateLimiter limiter = CreateLimiter(
            new DnsRateLimitOptions
            {
                Enable = false,
                BurstSize = 1,
                QueriesPerSecond = 0,
            }
        );

        for (int i = 0; i < 10; i++)
            Assert.True(await limiter.TryAcquireAsync(ClientA));
    }

    [Fact]
    public async Task AllowsUpToBurstSizeThenDeniesTheNextQuery()
    {
        TokenBucketDnsRateLimiter limiter = CreateLimiter(
            new DnsRateLimitOptions
            {
                Enable = true,
                BurstSize = 3,
                QueriesPerSecond = 0,
            }
        );

        Assert.True(await limiter.TryAcquireAsync(ClientA));
        Assert.True(await limiter.TryAcquireAsync(ClientA));
        Assert.True(await limiter.TryAcquireAsync(ClientA));
        Assert.False(await limiter.TryAcquireAsync(ClientA));
    }

    [Fact]
    public async Task RefillsTokensOverTime()
    {
        TokenBucketDnsRateLimiter limiter = CreateLimiter(
            new DnsRateLimitOptions
            {
                Enable = true,
                BurstSize = 1,
                QueriesPerSecond = 100,
            }
        );

        Assert.True(await limiter.TryAcquireAsync(ClientA));
        Assert.False(await limiter.TryAcquireAsync(ClientA));

        await Task.Delay(TimeSpan.FromMilliseconds(50));

        Assert.True(await limiter.TryAcquireAsync(ClientA));
    }

    [Fact]
    public async Task TracksEachClientAddressIndependently()
    {
        TokenBucketDnsRateLimiter limiter = CreateLimiter(
            new DnsRateLimitOptions
            {
                Enable = true,
                BurstSize = 1,
                QueriesPerSecond = 0,
            }
        );

        Assert.True(await limiter.TryAcquireAsync(ClientA));
        Assert.False(await limiter.TryAcquireAsync(ClientA));

        // ClientB has not queried before, so its bucket starts full whatever ClientA has spent.
        Assert.True(await limiter.TryAcquireAsync(ClientB));
    }

    [Fact]
    public async Task FailsOpenOnceTheTrackingTableIsFull()
    {
        TokenBucketDnsRateLimiter limiter = CreateLimiter(
            new DnsRateLimitOptions
            {
                Enable = true,
                BurstSize = 1,
                QueriesPerSecond = 0,
                MaxTrackedClients = 1,
            }
        );

        // ClientA takes the one tracking slot, then exhausts its budget.
        Assert.True(await limiter.TryAcquireAsync(ClientA));
        Assert.False(await limiter.TryAcquireAsync(ClientA));

        // ClientB would need a second slot, so it is let through (see
        // DnsRateLimitOptions.MaxTrackedClients).
        Assert.True(await limiter.TryAcquireAsync(ClientB));
    }

    [Fact]
    public void CleanupServiceSweepsStaleBucketsOnly()
    {
        TokenBucketDnsRateLimiter limiter = CreateLimiter(
            new DnsRateLimitOptions { Enable = true }
        );

        limiter.Buckets[ClientA] = new TokenBucketDnsRateLimiter.Bucket(1)
        {
            LastTouchedUtc = DateTimeOffset.UtcNow - TimeSpan.FromHours(1),
        };
        limiter.Buckets[ClientB] = new TokenBucketDnsRateLimiter.Bucket(1)
        {
            LastTouchedUtc = DateTimeOffset.UtcNow,
        };

        DnsRateLimitCleanupService cleanup = new(
            limiter,
            Options.Create(
                new DnsRateLimitOptions
                {
                    Enable = true,
                    TrackedClientExpiry = TimeSpan.FromMinutes(5),
                }
            )
        );

        cleanup.Sweep();

        Assert.False(limiter.Buckets.ContainsKey(ClientA));
        Assert.True(limiter.Buckets.ContainsKey(ClientB));
    }

    [Fact]
    public async Task ClientMatchingAnEnabledBypassRuleIsNeverLimitedEvenAfterExhaustingWhatWouldOtherwiseBeItsBudget()
    {
        FakeDnsAccessControlEngine accessControlEngine = new(
            DnsAccessControlCapabilityEnum.RateLimitBypass
        );
        accessControlEngine.Grant(ClientA);

        TokenBucketDnsRateLimiter limiter = CreateLimiter(
            new DnsRateLimitOptions
            {
                Enable = true,
                BurstSize = 1,
                QueriesPerSecond = 0,
            },
            accessControlEngine
        );

        // With these options a client without the bypass is denied after one query (see
        // AllowsUpToBurstSizeThenDeniesTheNextQuery). This client holds RateLimitBypass, so all
        // twenty succeed.
        for (int i = 0; i < 20; i++)
            Assert.True(await limiter.TryAcquireAsync(ClientA));

        // The bypass check runs before the bucket lookup, so no bucket is created.
        Assert.False(limiter.Buckets.ContainsKey(ClientA));
    }

    [Fact]
    public async Task ClientNotMatchingAnyBypassRuleIsStillLimitedNormally()
    {
        FakeDnsAccessControlEngine accessControlEngine = new(
            DnsAccessControlCapabilityEnum.RateLimitBypass
        );
        accessControlEngine.Grant(ClientA);

        TokenBucketDnsRateLimiter limiter = CreateLimiter(
            new DnsRateLimitOptions
            {
                Enable = true,
                BurstSize = 1,
                QueriesPerSecond = 0,
            },
            accessControlEngine
        );

        // ClientB has no bypass, so it is limited normally although ClientA is exempt.
        Assert.True(await limiter.TryAcquireAsync(ClientB));
        Assert.False(await limiter.TryAcquireAsync(ClientB));
    }

    [Fact]
    public async Task ANullAccessControlEngineLimitsEveryClientExactlyAsBeforeThisCapabilityExisted()
    {
        // No access-control engine (the default): no client is exempt.
        TokenBucketDnsRateLimiter limiter = CreateLimiter(
            new DnsRateLimitOptions
            {
                Enable = true,
                BurstSize = 1,
                QueriesPerSecond = 0,
            }
        );

        Assert.True(await limiter.TryAcquireAsync(ClientA));
        Assert.False(await limiter.TryAcquireAsync(ClientA));
    }
}

/// <summary>
///     This class tests the per-subnet layer of <see cref="TokenBucketDnsRateLimiter" />
///     (<see cref="TokenBucketDnsRateLimiter.CheckSubnetLimitAsync" />): IPv4 /24 and IPv6 /56
///     grouping, the truncate-or-drop split for UDP (with a fixed random source), dropping for
///     TCP, bypass CIDRs, failing open, cleanup, and independence from the per-IP layer. It
///     constructs the limiter directly.
/// </summary>
public class TokenBucketDnsRateLimiterSubnetTests
{
    // Two addresses in one IPv4 /24 (203.0.113.0/24): different IPs, same subnet.
    private static readonly IPAddress ClientA = IPAddress.Parse("203.0.113.10");
    private static readonly IPAddress ClientB = IPAddress.Parse("203.0.113.20");

    // A different IPv4 /24 (RFC 5737 TEST-NET-2), to show subnets are independent.
    private static readonly IPAddress ClientOtherSubnet = IPAddress.Parse("198.51.100.5");

    // Two addresses sharing one IPv6 /56 (RFC 3849 documentation prefix), and one outside it.
    private static readonly IPAddress Ipv6ClientA = IPAddress.Parse("2001:db8:0:0::1");
    private static readonly IPAddress Ipv6ClientB = IPAddress.Parse("2001:db8:0:ff::2");
    private static readonly IPAddress Ipv6ClientOtherSubnet = IPAddress.Parse("2001:db8:1::1");

    private static TokenBucketDnsRateLimiter CreateLimiter(
        DnsRateLimitOptions options,
        Func<double> randomSource = null
    ) => new(Options.Create(options), randomSource: randomSource);

    [Fact]
    public async Task DisabledLimiterAlwaysAllowsRegardlessOfVolume()
    {
        TokenBucketDnsRateLimiter limiter = CreateLimiter(
            new DnsRateLimitOptions { Enable = false, SubnetUdpQueryLimit = 1 }
        );

        for (int i = 0; i < 10; i++)
            Assert.Equal(
                DnsSubnetRateLimitDecisionEnum.Allow,
                await limiter.CheckSubnetLimitAsync(ClientA, isUdp: true)
            );
    }

    [Fact]
    public async Task AllowsUdpQueriesWithinTheSubnetsOwnLimit()
    {
        TokenBucketDnsRateLimiter limiter = CreateLimiter(
            new DnsRateLimitOptions
            {
                Enable = true,
                SubnetUdpQueryLimit = 3,
                SubnetSampleWindow = TimeSpan.FromHours(1),
            }
        );

        Assert.Equal(
            DnsSubnetRateLimitDecisionEnum.Allow,
            await limiter.CheckSubnetLimitAsync(ClientA, isUdp: true)
        );
        Assert.Equal(
            DnsSubnetRateLimitDecisionEnum.Allow,
            await limiter.CheckSubnetLimitAsync(ClientA, isUdp: true)
        );
        Assert.Equal(
            DnsSubnetRateLimitDecisionEnum.Allow,
            await limiter.CheckSubnetLimitAsync(ClientA, isUdp: true)
        );
    }

    [Fact]
    public async Task TwoDifferentIPsInTheSameIPv4Slash24ShareOneSubnetBudget()
    {
        TokenBucketDnsRateLimiter limiter = CreateLimiter(
            new DnsRateLimitOptions
            {
                Enable = true,
                SubnetUdpQueryLimit = 2,
                SubnetSampleWindow = TimeSpan.FromHours(1),
                SubnetTruncatePercentage = 0,
            }
        );

        Assert.Equal(
            DnsSubnetRateLimitDecisionEnum.Allow,
            await limiter.CheckSubnetLimitAsync(ClientA, isUdp: true)
        );
        Assert.Equal(
            DnsSubnetRateLimitDecisionEnum.Allow,
            await limiter.CheckSubnetLimitAsync(ClientB, isUdp: true)
        );

        // ClientA and ClientB are different exact addresses but share one /24 -- the shared
        // budget is now exhausted regardless of which of the two queries next.
        Assert.Equal(
            DnsSubnetRateLimitDecisionEnum.Drop,
            await limiter.CheckSubnetLimitAsync(ClientA, isUdp: true)
        );
    }

    [Fact]
    public async Task ADifferentIPv4Slash24IsBucketedEntirelyIndependently()
    {
        TokenBucketDnsRateLimiter limiter = CreateLimiter(
            new DnsRateLimitOptions
            {
                Enable = true,
                SubnetUdpQueryLimit = 1,
                SubnetSampleWindow = TimeSpan.FromHours(1),
                SubnetTruncatePercentage = 0,
            }
        );

        Assert.Equal(
            DnsSubnetRateLimitDecisionEnum.Allow,
            await limiter.CheckSubnetLimitAsync(ClientA, isUdp: true)
        );
        Assert.Equal(
            DnsSubnetRateLimitDecisionEnum.Drop,
            await limiter.CheckSubnetLimitAsync(ClientA, isUdp: true)
        );

        // A genuinely different /24 has never been queried before, so its own budget is untouched
        // by ClientA's subnet already being exhausted.
        Assert.Equal(
            DnsSubnetRateLimitDecisionEnum.Allow,
            await limiter.CheckSubnetLimitAsync(ClientOtherSubnet, isUdp: true)
        );
    }

    [Fact]
    public async Task TwoDifferentIPv6AddressesInTheSameSlash56ShareOneSubnetBudgetAndAThirdOutsideItDoesNot()
    {
        TokenBucketDnsRateLimiter limiter = CreateLimiter(
            new DnsRateLimitOptions
            {
                Enable = true,
                SubnetUdpQueryLimit = 2,
                SubnetSampleWindow = TimeSpan.FromHours(1),
                SubnetTruncatePercentage = 0,
            }
        );

        Assert.Equal(
            DnsSubnetRateLimitDecisionEnum.Allow,
            await limiter.CheckSubnetLimitAsync(Ipv6ClientA, isUdp: true)
        );
        Assert.Equal(
            DnsSubnetRateLimitDecisionEnum.Allow,
            await limiter.CheckSubnetLimitAsync(Ipv6ClientB, isUdp: true)
        );
        Assert.Equal(
            DnsSubnetRateLimitDecisionEnum.Drop,
            await limiter.CheckSubnetLimitAsync(Ipv6ClientA, isUdp: true)
        );

        // 2001:db8:1::1 falls outside 2001:db8:0::/56, so it is bucketed independently.
        Assert.Equal(
            DnsSubnetRateLimitDecisionEnum.Allow,
            await limiter.CheckSubnetLimitAsync(Ipv6ClientOtherSubnet, isUdp: true)
        );
    }

    [Fact]
    public async Task OverLimitUdpQueriesSplitBetweenTruncatedAndDroppedByTheConfiguredPercentage()
    {
        // A fixed random source makes the split deterministic: 0.3 is below the 50% cutoff
        // (truncated), 0.7 above it (dropped).
        Queue<double> randomValues = new([0.3, 0.7]);
        TokenBucketDnsRateLimiter limiter = CreateLimiter(
            new DnsRateLimitOptions
            {
                Enable = true,
                SubnetUdpQueryLimit = 0,
                SubnetSampleWindow = TimeSpan.FromHours(1),
                SubnetTruncatePercentage = 50,
            },
            randomSource: () => randomValues.Dequeue()
        );

        Assert.Equal(
            DnsSubnetRateLimitDecisionEnum.RespondTruncated,
            await limiter.CheckSubnetLimitAsync(ClientA, isUdp: true)
        );
        Assert.Equal(
            DnsSubnetRateLimitDecisionEnum.Drop,
            await limiter.CheckSubnetLimitAsync(ClientA, isUdp: true)
        );
    }

    [Fact]
    public async Task OverLimitTcpQueriesAreAlwaysDroppedNeverTruncated()
    {
        // Truncating to force a TCP retry means nothing to a client already on TCP (see
        // DnsSubnetRateLimitDecisionEnum.RespondTruncated).
        TokenBucketDnsRateLimiter limiter = CreateLimiter(
            new DnsRateLimitOptions
            {
                Enable = true,
                SubnetTcpQueryLimit = 0,
                SubnetSampleWindow = TimeSpan.FromHours(1),
                SubnetTruncatePercentage = 100,
            }
        );

        Assert.Equal(
            DnsSubnetRateLimitDecisionEnum.Drop,
            await limiter.CheckSubnetLimitAsync(ClientA, isUdp: false)
        );
    }

    [Fact]
    public async Task AllowlistedCidrBypassesTheSubnetCheckEntirely()
    {
        TokenBucketDnsRateLimiter limiter = CreateLimiter(
            new DnsRateLimitOptions
            {
                Enable = true,
                SubnetUdpQueryLimit = 1,
                SubnetSampleWindow = TimeSpan.FromHours(1),
                SubnetBypassCidrs = ["203.0.113.0/24"],
            }
        );

        for (int i = 0; i < 10; i++)
            Assert.Equal(
                DnsSubnetRateLimitDecisionEnum.Allow,
                await limiter.CheckSubnetLimitAsync(ClientA, isUdp: true)
            );
    }

    [Fact]
    public async Task AnIPv4MappedBypassCidrCoversTheSameHostsAsTheIPv4Form()
    {
        TokenBucketDnsRateLimiter limiter = CreateLimiter(
            new DnsRateLimitOptions
            {
                Enable = true,
                SubnetUdpQueryLimit = 1,
                SubnetSampleWindow = TimeSpan.FromHours(1),
                SubnetTruncatePercentage = 0,
                SubnetBypassCidrs = ["::ffff:203.0.113.0/120"],
            }
        );

        for (int i = 0; i < 10; i++)
            Assert.Equal(
                DnsSubnetRateLimitDecisionEnum.Allow,
                await limiter.CheckSubnetLimitAsync(ClientA, isUdp: true)
            );

        // A different /24 is not covered, so its second query is over the limit of 1.
        IPAddress other = IPAddress.Parse("198.51.100.5");
        await limiter.CheckSubnetLimitAsync(other, isUdp: true);
        Assert.Equal(
            DnsSubnetRateLimitDecisionEnum.Drop,
            await limiter.CheckSubnetLimitAsync(other, isUdp: true)
        );
    }

    [Fact]
    public async Task FailsOpenOnceTheSubnetTrackingTableIsFull()
    {
        TokenBucketDnsRateLimiter limiter = CreateLimiter(
            new DnsRateLimitOptions
            {
                Enable = true,
                SubnetUdpQueryLimit = 0,
                SubnetSampleWindow = TimeSpan.FromHours(1),
                SubnetTruncatePercentage = 0,
                MaxTrackedClients = 1,
            }
        );

        // ClientA's /24 takes the one subnet slot and is over its limit of 0 at once.
        Assert.Equal(
            DnsSubnetRateLimitDecisionEnum.Drop,
            await limiter.CheckSubnetLimitAsync(ClientA, isUdp: true)
        );

        // A second /24 would need a second slot, so it is let through, as TryAcquireAsync does
        // for a full per-IP table.
        Assert.Equal(
            DnsSubnetRateLimitDecisionEnum.Allow,
            await limiter.CheckSubnetLimitAsync(ClientOtherSubnet, isUdp: true)
        );
    }

    [Fact]
    public void CleanupServiceSweepsStaleSubnetWindowsOnly()
    {
        TokenBucketDnsRateLimiter limiter = CreateLimiter(
            new DnsRateLimitOptions { Enable = true }
        );

        IPAddress staleSubnetKey = IPAddress.Parse("203.0.113.0");
        IPAddress freshSubnetKey = IPAddress.Parse("198.51.100.0");

        limiter.SubnetWindows[staleSubnetKey] = new TokenBucketDnsRateLimiter.SubnetWindow(0)
        {
            LastTouchedUtc = DateTimeOffset.UtcNow - TimeSpan.FromHours(1),
        };
        limiter.SubnetWindows[freshSubnetKey] = new TokenBucketDnsRateLimiter.SubnetWindow(0)
        {
            LastTouchedUtc = DateTimeOffset.UtcNow,
        };

        DnsRateLimitCleanupService cleanup = new(
            limiter,
            Options.Create(
                new DnsRateLimitOptions
                {
                    Enable = true,
                    TrackedClientExpiry = TimeSpan.FromMinutes(5),
                }
            )
        );

        cleanup.Sweep();

        Assert.False(limiter.SubnetWindows.ContainsKey(staleSubnetKey));
        Assert.True(limiter.SubnetWindows.ContainsKey(freshSubnetKey));
    }

    [Fact]
    public async Task ThePerIpTokenBucketLayerIsCompletelyUnaffectedByTheSubnetLayer()
    {
        // The two layers share the class and the options but no state: exhausting one must not
        // affect the other.
        TokenBucketDnsRateLimiter limiter = CreateLimiter(
            new DnsRateLimitOptions
            {
                Enable = true,
                BurstSize = 5,
                QueriesPerSecond = 0,
                SubnetUdpQueryLimit = 0,
                SubnetSampleWindow = TimeSpan.FromHours(1),
                SubnetTruncatePercentage = 0,
            }
        );

        // Exhaust ClientA's subnet budget first.
        Assert.Equal(
            DnsSubnetRateLimitDecisionEnum.Drop,
            await limiter.CheckSubnetLimitAsync(ClientA, isUdp: true)
        );

        // The per-IP bucket is untouched, so it still grants its full burst.
        for (int i = 0; i < 5; i++)
            Assert.True(await limiter.TryAcquireAsync(ClientA));
        Assert.False(await limiter.TryAcquireAsync(ClientA));

        // And the reverse: exhausting the per-IP bucket leaves the subnet window as it was.
        Assert.Equal(
            DnsSubnetRateLimitDecisionEnum.Drop,
            await limiter.CheckSubnetLimitAsync(ClientA, isUdp: true)
        );
    }
}
