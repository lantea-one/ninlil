using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.AccessControl.Extension;
using Ninlil.Dns.Server.AccessControl.Tests.Fakes;
using Ninlil.Dns.Server.Core.AccessControl;
using Ninlil.Dns.Server.Core.Extension;

// Define our namespace.
namespace Ninlil.Dns.Server.AccessControl.Tests;

/// <summary>
///     This class tests <see cref="DnsAccessControlRuleBackstopService" /> in a generic host with
///     a counting fake repository and a <see cref="TickingTimeProvider" />: a rule added to the
///     repository without any event, as another instance sharing the database would add it, is in
///     effect after the fake clock passes the reload interval.
/// </summary>
public sealed class DnsAccessControlRuleBackstopTests : IAsyncLifetime
{
    private static readonly IPAddress ExemptAddress = IPAddress.Parse("192.0.2.10");

    private readonly CountingDnsAccessControlRuleRepository _repository = new();
    private readonly TickingTimeProvider _clock = new();
    private IHost _host;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IDnsAccessControlRuleRepository>(_repository);
        builder.Services.AddSingleton<TimeProvider>(_clock);
        builder.Services.AddNinlilDnsServerCore(builder.Configuration);
        builder.Services.AddNinlilDnsServerAccessControl();

        _host = builder.Build();
        await _host.StartAsync();
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task Reload_AfterAnotherInstanceAddedARule_PutsTheRuleInEffect()
    {
        IDnsAccessControlEngine engine =
            _host.Services.GetRequiredService<IDnsAccessControlEngine>();
        DnsAccessControlRuleBackstopService backstop = _host
            .Services.GetServices<IHostedService>()
            .OfType<DnsAccessControlRuleBackstopService>()
            .Single();

        Assert.False(
            await engine.HasCapabilityAsync(
                ExemptAddress,
                DnsAccessControlCapabilityEnum.RateLimitBypass
            )
        );

        _repository.Seed(
            new DnsAccessControlRuleEntity
            {
                Id = Guid.NewGuid(),
                Name = "Added on the other instance",
                Cidr = "192.0.2.10/32",
                Capabilities = DnsAccessControlCapabilityEnum.RateLimitBypass,
                IsDisabled = false,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );

        // The service creates its timer on its background task; a tick before that is lost.
        for (int attempt = 0; attempt < 500 && _clock.TimerCount == 0; attempt++)
            await Task.Delay(20);

        long before = backstop.CompletedReloadCount;
        _clock.Advance(TimeSpan.FromSeconds(61));

        for (int attempt = 0; attempt < 500 && backstop.CompletedReloadCount == before; attempt++)
            await Task.Delay(20);

        Assert.True(
            await engine.HasCapabilityAsync(
                ExemptAddress,
                DnsAccessControlCapabilityEnum.RateLimitBypass
            )
        );
    }
}
