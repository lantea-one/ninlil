using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.AccessControl.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.AccessControl.Tests;

/// <summary>
///     This class tests how <see cref="DnsAccessControlRuleCache" /> turns the rules' CIDR text
///     into parsed networks when it loads: a rule whose text does not parse matches nothing and
///     does not stop the other rules, and a capability check allocates nothing, which shows the
///     text is not parsed again per query. It uses the counting fake repository and no host.
/// </summary>
public sealed class DnsAccessControlRuleCacheParsedRuleTests
{
    private static async Task<DnsAccessControlRuleCache> LoadAsync(
        params (string Cidr, DnsAccessControlCapabilityEnum Capabilities)[] rules
    )
    {
        CountingDnsAccessControlRuleRepository repository = new();

        foreach ((string cidr, DnsAccessControlCapabilityEnum capabilities) in rules)
            repository.Seed(
                new DnsAccessControlRuleEntity
                {
                    Id = Guid.NewGuid(),
                    Name = cidr ?? "none",
                    Cidr = cidr,
                    Capabilities = capabilities,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                }
            );

        ServiceCollection services = new();
        services.AddSingleton<IDnsAccessControlRuleRepository>(repository);
        ServiceProvider provider = services.BuildServiceProvider();

        DnsAccessControlRuleCache cache = new(provider.GetRequiredService<IServiceScopeFactory>());
        await cache.ReloadAsync();
        return cache;
    }

    [Fact]
    public async Task HasCapability_ARuleThatDoesNotParse_MatchesNothingAndTheOthersStillMatch()
    {
        DnsAccessControlRuleCache cache = await LoadAsync(
            ("junk", DnsAccessControlCapabilityEnum.RecursionAccess),
            (null, DnsAccessControlCapabilityEnum.RecursionAccess),
            ("10.0.0.0/99", DnsAccessControlCapabilityEnum.RecursionAccess),
            ("203.0.113.0/24", DnsAccessControlCapabilityEnum.RecursionAccess)
        );

        Assert.True(
            cache.HasCapability(
                IPAddress.Parse("203.0.113.9"),
                DnsAccessControlCapabilityEnum.RecursionAccess
            )
        );
        Assert.False(
            cache.HasCapability(
                IPAddress.Parse("10.0.0.1"),
                DnsAccessControlCapabilityEnum.RecursionAccess
            )
        );
    }

    [Fact]
    public async Task HasCapability_MatchesByCapabilityAndByNetworkForEveryAddressForm()
    {
        DnsAccessControlRuleCache cache = await LoadAsync(
            ("192.0.2.0/24", DnsAccessControlCapabilityEnum.RecursionAccess),
            ("::ffff:198.51.100.0/120", DnsAccessControlCapabilityEnum.RateLimitBypass),
            ("fe80::1", DnsAccessControlCapabilityEnum.RecursionAccess)
        );

        Assert.True(
            cache.HasCapability(
                IPAddress.Parse("::ffff:192.0.2.5"),
                DnsAccessControlCapabilityEnum.RecursionAccess
            )
        );
        Assert.False(
            cache.HasCapability(
                IPAddress.Parse("192.0.2.5"),
                DnsAccessControlCapabilityEnum.RateLimitBypass
            )
        );
        Assert.True(
            cache.HasCapability(
                IPAddress.Parse("198.51.100.4"),
                DnsAccessControlCapabilityEnum.RateLimitBypass
            )
        );
        Assert.True(
            cache.HasCapability(
                IPAddress.Parse("fe80::1%3"),
                DnsAccessControlCapabilityEnum.RecursionAccess
            )
        );
    }

    [Fact]
    public async Task HasCapability_AfterTheLoad_AllocatesNothingPerQuery()
    {
        DnsAccessControlRuleCache cache = await LoadAsync(
            ("10.0.0.0/8", DnsAccessControlCapabilityEnum.RecursionAccess),
            ("2001:db8::/32", DnsAccessControlCapabilityEnum.RecursionAccess),
            ("192.0.2.0/24", DnsAccessControlCapabilityEnum.RecursionAccess)
        );
        IPAddress client = IPAddress.Parse("192.0.2.9");

        // Warm up so JIT and lazy statics do not count.
        for (int i = 0; i < 100; i++)
            Assert.True(
                cache.HasCapability(client, DnsAccessControlCapabilityEnum.RecursionAccess)
            );

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 1000; i++)
            cache.HasCapability(client, DnsAccessControlCapabilityEnum.RecursionAccess);

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
