using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.AccessControl.Extension;
using Ninlil.Dns.Server.AccessControl.Tests.Fakes;
using Ninlil.Dns.Server.Core.AccessControl;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Extension;

// Define our namespace.
namespace Ninlil.Dns.Server.AccessControl.Tests;

/// <summary>
///     This class proves, against the real <see cref="DnsAccessControlRuleCache" />,
///     <see cref="DnsAccessControlEngine" />, and event bus (<see cref="IDnsServerEventPublisher" />/
///     <see cref="DnsServerEventDispatcher" />/<see cref="DnsAccessControlRuleChangedEventSubscriber" />)
///     -- not a mock, not the throwaway spike that first proved this design out -- the exact claim
///     that spike made informally: once warm, capability resolution
///     performs zero synchronous database I/O, and a rule change is reflected only after a real, published
///     <see cref="DnsAccessControlRuleChangedEvent" /> triggers a single whole-set reload, never a
///     per-lookup read.
/// </summary>
public sealed class DnsAccessControlRuleCacheEventTests : IAsyncLifetime
{
    private static readonly IPAddress TrustedAddress = IPAddress.Parse("203.0.113.10");
    private static readonly IPAddress UntrustedAddress = IPAddress.Parse("198.51.100.10");
    private static readonly IPAddress ExemptAddress = IPAddress.Parse("192.0.2.10");

    private readonly CountingDnsAccessControlRuleRepository _repository = new();

    private IHost _host;

    public Task InitializeAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();

        // A fake repository, not a real database, backs this test -- the same substitution
        // the original throwaway spike made, so ListEnabledAsyncCallCount can prove exactly how
        // many times the cache actually reads from it.
        builder.Services.AddSingleton<IDnsAccessControlRuleRepository>(_repository);

        builder.Services.AddNinlilDnsServerCore(builder.Configuration);
        builder.Services.AddNinlilDnsServerAccessControl();

        // Seeded before the host starts, so DnsAccessControlRuleCache.StartAsync's own initial
        // load (run synchronously as part of _host.StartAsync(), the same guarantee
        // SignedZoneCacheEventTests documents for an ordinary IHostedService) picks it up as call
        // #1.
        _repository.Seed(
            new DnsAccessControlRuleEntity
            {
                Id = Guid.NewGuid(),
                Name = "Trusted LAN",
                Cidr = "203.0.113.0/24",
                Capabilities = DnsAccessControlCapabilityEnum.RecursionAccess,
                IsDisabled = false,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        _repository.Seed(
            new DnsAccessControlRuleEntity
            {
                Id = Guid.NewGuid(),
                Name = "Disabled rule, never consulted",
                Cidr = "198.51.100.0/24",
                Capabilities =
                    DnsAccessControlCapabilityEnum.RecursionAccess
                    | DnsAccessControlCapabilityEnum.RateLimitBypass,
                IsDisabled = true,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );

        _host = builder.Build();

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task InitialLoadHappensOnceAtStartupAndCapabilityChecksNeverReadTheRepositoryAgain()
    {
        await _host.StartAsync();

        Assert.Equal(1, _repository.ListEnabledAsyncCallCount);

        IDnsAccessControlEngine engine =
            _host.Services.GetRequiredService<IDnsAccessControlEngine>();

        // A real, matching grant.
        for (int i = 0; i < 10_000; i++)
            Assert.True(
                await engine.HasCapabilityAsync(
                    TrustedAddress,
                    DnsAccessControlCapabilityEnum.RecursionAccess
                )
            );

        // A real, non-matching client.
        for (int i = 0; i < 10_000; i++)
            Assert.False(
                await engine.HasCapabilityAsync(
                    UntrustedAddress,
                    DnsAccessControlCapabilityEnum.RecursionAccess
                )
            );

        // A client matching a disabled rule's own network is treated exactly as if that rule did
        // not exist at all -- DnsAccessControlRuleEntity.IsDisabled's own documented contract.
        for (int i = 0; i < 10_000; i++)
            Assert.False(
                await engine.HasCapabilityAsync(
                    UntrustedAddress,
                    DnsAccessControlCapabilityEnum.RateLimitBypass
                )
            );

        // 30,000 real capability checks against the real cache/engine, and the fake repository
        // was still only ever read once -- the initial load.
        Assert.Equal(1, _repository.ListEnabledAsyncCallCount);
    }

    [Fact]
    public async Task PublishingAChangedEventReloadsTheWholeSetExactlyOnce()
    {
        await _host.StartAsync();
        Assert.Equal(1, _repository.ListEnabledAsyncCallCount);

        IDnsAccessControlEngine engine =
            _host.Services.GetRequiredService<IDnsAccessControlEngine>();

        // Not yet granted -- this rule does not exist in the cache's own snapshot yet.
        Assert.False(
            await engine.HasCapabilityAsync(
                ExemptAddress,
                DnsAccessControlCapabilityEnum.RateLimitBypass
            )
        );

        Guid newRuleId = Guid.NewGuid();
        _repository.Seed(
            new DnsAccessControlRuleEntity
            {
                Id = newRuleId,
                Name = "Rate-limit-exempt monitoring host",
                Cidr = "192.0.2.10/32",
                Capabilities = DnsAccessControlCapabilityEnum.RateLimitBypass,
                IsDisabled = false,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );

        // A real publish through the real event bus -- not a direct method call into the cache.
        IDnsServerEventPublisher publisher =
            _host.Services.GetRequiredService<IDnsServerEventPublisher>();
        publisher.Publish(new DnsAccessControlRuleChangedEvent(DateTimeOffset.UtcNow, newRuleId));

        // DnsServerEventDispatcher delivers off the caller's own call stack, so this test polls
        // for the effect rather than asserting it immediately -- the same idiom
        // CloudDnsZoneSyncFailureEventTests/AcmeCertificateRenewalServiceEventTests already use
        // for an equivalent asynchronously-delivered event.
        bool granted = false;
        for (int attempt = 0; attempt < 100 && !granted; attempt++)
        {
            granted = await engine.HasCapabilityAsync(
                ExemptAddress,
                DnsAccessControlCapabilityEnum.RateLimitBypass
            );

            if (!granted)
                await Task.Delay(50);
        }

        Assert.True(granted);

        // Exactly one reload happened -- the whole set was re-read once, not once per rule and
        // not once per lookup.
        Assert.Equal(2, _repository.ListEnabledAsyncCallCount);

        // Further lookups after the reload still cost nothing extra.
        for (int i = 0; i < 10_000; i++)
            await engine.HasCapabilityAsync(
                ExemptAddress,
                DnsAccessControlCapabilityEnum.RateLimitBypass
            );

        Assert.Equal(2, _repository.ListEnabledAsyncCallCount);
    }
}
