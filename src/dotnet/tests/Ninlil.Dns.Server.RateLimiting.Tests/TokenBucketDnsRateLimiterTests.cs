using System.Net;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.AccessControl;
using Ninlil.Dns.Server.Core.RateLimiting;
using Ninlil.Dns.Server.RateLimiting.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.RateLimiting.Tests;

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

        // ClientB has never queried before, so its own bucket starts fully stocked regardless
        // of ClientA's already-exhausted budget.
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

        // ClientA fills the one available tracking slot, then exhausts its own budget.
        Assert.True(await limiter.TryAcquireAsync(ClientA));
        Assert.False(await limiter.TryAcquireAsync(ClientA));

        // ClientB would need a second tracking slot that does not exist; failing open (rather
        // than denying) is the deliberate, documented choice so this limiter cannot itself
        // become an unbounded-memory denial-of-service target.
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

        // Every one of these would exhaust, and then be denied by, an ordinary 1-token bucket --
        // proved by AllowsUpToBurstSizeThenDeniesTheNextQuery above using the exact same options
        // for a non-exempt client. This client is granted RateLimitBypass, so every single one of
        // these must still succeed, against the real token-bucket implementation, not a mock.
        for (int i = 0; i < 20; i++)
            Assert.True(await limiter.TryAcquireAsync(ClientA));

        // The bypass check runs before the bucket lookup/creation, so an exempt client's own
        // bucket must never even be created.
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

        // ClientB is not granted the bypass, so it is limited exactly as before this capability
        // existed, even though the same limiter instance also holds a granted client.
        Assert.True(await limiter.TryAcquireAsync(ClientB));
        Assert.False(await limiter.TryAcquireAsync(ClientB));
    }

    [Fact]
    public async Task ANullAccessControlEngineLimitsEveryClientExactlyAsBeforeThisCapabilityExisted()
    {
        // The default -- every existing caller that constructs this limiter directly (including
        // every other test in this file) keeps compiling and behaving unchanged.
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
///     This class covers <see cref="TokenBucketDnsRateLimiter.CheckSubnetLimitAsync" /> -- the
///     second, independent, CIDR-subnet-bucketed rate-limit layer -- kept separate from
///     <see cref="TokenBucketDnsRateLimiterTests" /> itself so each file stays focused on one of
///     the two genuinely independent layers this class implements.
/// </summary>
public class TokenBucketDnsRateLimiterSubnetTests
{
    // Both share one IPv4 /24 (203.0.113.0/24) -- the exact "different exact IP, same subnet"
    // shape this layer exists to catch.
    private static readonly IPAddress ClientA = IPAddress.Parse("203.0.113.10");
    private static readonly IPAddress ClientB = IPAddress.Parse("203.0.113.20");

    // A separate IPv4 /24 (RFC 5737 TEST-NET-2), for proving cross-subnet independence.
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
        // A fixed randomSource makes the split's own threshold comparison fully deterministic:
        // 0.3 always lands below a 50% cutoff (truncated), 0.7 always lands above it (dropped).
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
        // TC-forcing-a-TCP-retry is meaningless to a client already using TCP -- see
        // DnsSubnetRateLimitDecisionEnum.RespondTruncated's own doc comment.
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

        // ClientA's own /24 fills the one available subnet-tracking slot, then exhausts its own
        // budget (limit of 0).
        Assert.Equal(
            DnsSubnetRateLimitDecisionEnum.Drop,
            await limiter.CheckSubnetLimitAsync(ClientA, isUdp: true)
        );

        // A second, genuinely different /24 would need a second tracking slot that does not
        // exist; failing open mirrors TryAcquireAsync's own MaxTrackedClients posture exactly.
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
        // Both layers share the same class and DnsRateLimitOptions, but nothing else -- a real
        // regression test that exhausting one never touches the other, not just an assertion
        // that they happen not to share fields today.
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

        // Exhaust ClientA's own subnet budget first.
        Assert.Equal(
            DnsSubnetRateLimitDecisionEnum.Drop,
            await limiter.CheckSubnetLimitAsync(ClientA, isUdp: true)
        );

        // The exact-IP token bucket has never been touched by any of the above, so it still
        // grants its own full, independent burst allowance.
        for (int i = 0; i < 5; i++)
            Assert.True(await limiter.TryAcquireAsync(ClientA));
        Assert.False(await limiter.TryAcquireAsync(ClientA));

        // And the reverse: exhausting the per-IP bucket above must not have touched the subnet
        // window's own already-exhausted state.
        Assert.Equal(
            DnsSubnetRateLimitDecisionEnum.Drop,
            await limiter.CheckSubnetLimitAsync(ClientA, isUdp: true)
        );
    }
}
