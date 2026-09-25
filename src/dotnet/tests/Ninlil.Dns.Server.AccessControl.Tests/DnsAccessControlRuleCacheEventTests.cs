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
///     This class tests <see cref="DnsAccessControlRuleCache" /> and
///     <see cref="DnsAccessControlEngine" /> together with the event bus
///     (<see cref="IDnsServerEventPublisher" />, <see cref="DnsServerEventDispatcher" /> and
///     <see cref="DnsAccessControlRuleChangedEventSubscriber" />) in a generic host, with a
///     counting fake repository in place of the database. It checks that, once loaded,
///     capability checks never read the repository, and that a published
///     <see cref="DnsAccessControlRuleChangedEvent" /> causes one whole-set reload.
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

        // A counting fake stands in for the database, so ListEnabledAsyncCallCount shows how
        // many times the cache reads it.
        builder.Services.AddSingleton<IDnsAccessControlRuleRepository>(_repository);

        builder.Services.AddNinlilDnsServerCore(builder.Configuration);
        builder.Services.AddNinlilDnsServerAccessControl();

        // Seeded before the host starts, so the cache's initial load in StartAsync (awaited by
        // _host.StartAsync()) picks these rules up as read number 1.
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

        // A matching grant.
        for (int i = 0; i < 10_000; i++)
            Assert.True(
                await engine.HasCapabilityAsync(
                    TrustedAddress,
                    DnsAccessControlCapabilityEnum.RecursionAccess
                )
            );

        // A client no enabled rule matches.
        for (int i = 0; i < 10_000; i++)
            Assert.False(
                await engine.HasCapabilityAsync(
                    UntrustedAddress,
                    DnsAccessControlCapabilityEnum.RecursionAccess
                )
            );

        // A client in a disabled rule's network is treated as if the rule did not exist.
        for (int i = 0; i < 10_000; i++)
            Assert.False(
                await engine.HasCapabilityAsync(
                    UntrustedAddress,
                    DnsAccessControlCapabilityEnum.RateLimitBypass
                )
            );

        // 30,000 checks later, the repository has still been read only once: the initial load.
        Assert.Equal(1, _repository.ListEnabledAsyncCallCount);
    }

    [Fact]
    public async Task PublishingAChangedEventReloadsTheWholeSetExactlyOnce()
    {
        await _host.StartAsync();
        Assert.Equal(1, _repository.ListEnabledAsyncCallCount);

        IDnsAccessControlEngine engine =
            _host.Services.GetRequiredService<IDnsAccessControlEngine>();

        // Not granted yet: the rule is not in the cache's snapshot.
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

        // Publish through the event bus rather than calling the cache directly.
        IDnsServerEventPublisher publisher =
            _host.Services.GetRequiredService<IDnsServerEventPublisher>();
        publisher.Publish(new DnsAccessControlRuleChangedEvent(DateTimeOffset.UtcNow, newRuleId));

        // DnsServerEventDispatcher delivers events in the background, so poll for the effect
        // instead of asserting it immediately.
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

        // One reload: the whole set was read once, not once per rule or per lookup.
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
