using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Server.Core.DnsUpdate;
using Ninlil.Dhcp.Server.Core.Extension;
using Ninlil.Dhcp.Server.DnsIntegration.Extension;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Xunit;

// Define our namespace.
namespace Ninlil.Dhcp.Server.DnsIntegration.Tests;

/// <summary>
///     This class covers <see cref="DhcpDnsUpdatePolicy" /> over a temporary SQLite file: a scope's
///     host names are registered only when its <c>DnsZoneId</c> names an existing Primary or
///     CloudPrimary zone, and it replaces the engine's default policy in either registration order.
/// </summary>
public sealed class DhcpDnsUpdatePolicyTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dhcp-dns-policy-tests-{Guid.NewGuid()}.sqlite3"
    );

    private IHost _host;

    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);
        _host = builder.Build();

        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<NinlilDnsContext>()
            .Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync()
    {
        _host.Dispose();

        TemporaryFiles.DeleteSqliteDatabase(_databasePath);

        return Task.CompletedTask;
    }

    private async Task<Guid> SeedZoneAsync(ZoneTypeEnum type)
    {
        Guid zoneId = Guid.NewGuid();
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<IZoneRepository>()
            .AddAsync(
                new ZoneEntity
                {
                    Id = zoneId,
                    Name = $"{type}.example".ToLowerInvariant(),
                    Type = type,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                }
            );
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        return zoneId;
    }

    private async Task<bool> AskAsync(DhcpScopeEntity scope)
    {
        await using AsyncServiceScope serviceScope = _host.Services.CreateAsyncScope();
        DhcpDnsUpdatePolicy policy = new(
            serviceScope.ServiceProvider.GetRequiredService<IZoneRepository>()
        );
        return await policy.RegistersHostNamesAsync(scope);
    }

    [Theory]
    [InlineData(ZoneTypeEnum.Primary, true)]
    [InlineData(ZoneTypeEnum.CloudPrimary, true)]
    [InlineData(ZoneTypeEnum.Secondary, false)]
    public async Task RegistersHostNamesAsync_ForAScopeWithAZone_FollowsTheZoneType(
        ZoneTypeEnum type,
        bool expected
    )
    {
        Guid zoneId = await SeedZoneAsync(type);

        Assert.Equal(expected, await AskAsync(new DhcpScopeEntity { DnsZoneId = zoneId }));
    }

    [Fact]
    public async Task RegistersHostNamesAsync_ForAScopeWithoutAZone_IsFalse() =>
        Assert.False(await AskAsync(new DhcpScopeEntity { DnsZoneId = null }));

    [Fact]
    public async Task RegistersHostNamesAsync_ForAZoneThatNoLongerExists_IsFalse() =>
        Assert.False(await AskAsync(new DhcpScopeEntity { DnsZoneId = Guid.NewGuid() }));

    [Fact]
    public void AddNinlilDhcpDnsIntegration_InEitherOrderWithTheEngine_ReplacesTheDefaultPolicy()
    {
        foreach (bool integrationFirst in new[] { true, false })
        {
            ServiceCollection services = new();
            Microsoft.Extensions.Configuration.IConfiguration configuration =
                new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();

            if (integrationFirst)
            {
                services.AddNinlilDhcpDnsIntegration();
                services.AddNinlilDhcpv6ServerCore(configuration);
            }
            else
            {
                services.AddNinlilDhcpv6ServerCore(configuration);
                services.AddNinlilDhcpDnsIntegration();
            }

            ServiceDescriptor descriptor = Assert.Single(
                services,
                candidate => candidate.ServiceType == typeof(IDhcpDnsUpdatePolicy)
            );
            Assert.Equal(typeof(DhcpDnsUpdatePolicy), descriptor.ImplementationType);
        }
    }
}
