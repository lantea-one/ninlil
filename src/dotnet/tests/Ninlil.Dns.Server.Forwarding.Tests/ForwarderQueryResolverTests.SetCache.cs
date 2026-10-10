using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Forwarding.Caching;
using Ninlil.Dns.Server.Forwarding.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Tests;

/// <summary>
///     This part of <see cref="ForwarderQueryResolverTests" /> tests the forwarder-set cache
///     (<see cref="ForwarderSetCache" />): a zone or global forwarder saved through the real EF
///     save interceptor applies to the very next query (read-your-own-writes), zone names match
///     whatever their case or trailing dot, and a forwarded-response cache hit runs no database
///     command at all (counted by <see cref="CommandCounter" />).
/// </summary>
public sealed partial class ForwarderQueryResolverTests
{
    /// <summary>
    ///     This method changes the stored zone with the given name and saves it, as the zone
    ///     controller does.
    /// </summary>
    private async Task UpdateZoneAsync(string zoneName, Action<ZoneEntity> change)
    {
        using IServiceScope scope = _provider.CreateScope();
        IZoneRepository repository = scope.ServiceProvider.GetRequiredService<IZoneRepository>();
        ZoneEntity zone = await repository.FindByNameAsync(zoneName);
        change(zone);
        repository.Update(zone);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    /// <summary>
    ///     This method disables every global forwarder and saves.
    /// </summary>
    private async Task DisableGlobalForwardersAsync()
    {
        using IServiceScope scope = _provider.CreateScope();
        IDnsForwarderRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsForwarderRepository>();

        foreach (DnsForwarderEntity forwarder in await repository.ListAsync())
        {
            forwarder.IsDisabled = true;
            repository.Update(forwarder);
        }

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    private static async Task<bool> IsHandledAsync(ForwarderQueryResolver resolver, string name) =>
        (await resolver.TryResolveAsync(BuildContext(BuildQuery(1, name)))).IsHandled;

    [Fact]
    public async Task SetCache_ARenamedForwarderZone_StopsMatchingOnTheNextQuery()
    {
        using FakeUdpUpstream globalUpstream = FakeUdpUpstream.Start(
            BuildPositiveResponse(IPAddress.Parse("192.0.2.1"))
        );
        using FakeUdpUpstream zoneUpstream = FakeUdpUpstream.Start(
            BuildPositiveResponse(IPAddress.Parse("203.0.113.99"))
        );
        await SeedForwarderAsync($"127.0.0.1:{globalUpstream.Port}");
        await SeedForwarderZoneAsync("corp.example", $"127.0.0.1:{zoneUpstream.Port}");
        ForwarderQueryResolver resolver = CreateResolver();

        Assert.True(await IsHandledAsync(resolver, "www.corp.example."));
        Assert.Equal(1, zoneUpstream.QueryCount);

        await UpdateZoneAsync("corp.example", zone => zone.Name = "other.example");

        Assert.True(await IsHandledAsync(resolver, "www.corp.example."));
        Assert.Equal(1, zoneUpstream.QueryCount);
        Assert.Equal(1, globalUpstream.QueryCount);

        Assert.True(await IsHandledAsync(resolver, "www.other.example."));
        Assert.Equal(2, zoneUpstream.QueryCount);
    }

    [Fact]
    public async Task SetCache_ADisabledForwarderZone_StopsMatchingOnTheNextQuery()
    {
        using FakeUdpUpstream globalUpstream = FakeUdpUpstream.Start(
            BuildPositiveResponse(IPAddress.Parse("192.0.2.1"))
        );
        using FakeUdpUpstream zoneUpstream = FakeUdpUpstream.Start(
            BuildPositiveResponse(IPAddress.Parse("203.0.113.99"))
        );
        await SeedForwarderAsync($"127.0.0.1:{globalUpstream.Port}");
        await SeedForwarderZoneAsync("corp.example", $"127.0.0.1:{zoneUpstream.Port}");
        ForwarderQueryResolver resolver = CreateResolver();
        Assert.True(await IsHandledAsync(resolver, "www.corp.example."));
        Assert.Equal(1, zoneUpstream.QueryCount);

        await UpdateZoneAsync("corp.example", zone => zone.IsDisabled = true);

        Assert.True(await IsHandledAsync(resolver, "www.corp.example."));
        Assert.Equal(1, zoneUpstream.QueryCount);
        Assert.Equal(1, globalUpstream.QueryCount);
    }

    [Fact]
    public async Task SetCache_AForwarderZoneAddedAfterTheFirstQuery_IsUsedByTheNextQuery()
    {
        using FakeUdpUpstream globalUpstream = FakeUdpUpstream.Start(
            BuildPositiveResponse(IPAddress.Parse("192.0.2.1"))
        );
        using FakeUdpUpstream zoneUpstream = FakeUdpUpstream.Start(
            BuildPositiveResponse(IPAddress.Parse("203.0.113.99"))
        );
        await SeedForwarderAsync($"127.0.0.1:{globalUpstream.Port}");
        ForwarderQueryResolver resolver = CreateResolver();
        Assert.True(await IsHandledAsync(resolver, "www.corp.example."));
        Assert.Equal(1, globalUpstream.QueryCount);

        await SeedForwarderZoneAsync("corp.example", $"127.0.0.1:{zoneUpstream.Port}");

        Assert.True(await IsHandledAsync(resolver, "www.corp.example."));
        Assert.Equal(1, zoneUpstream.QueryCount);
        Assert.Equal(1, globalUpstream.QueryCount);
    }

    [Fact]
    public async Task SetCache_AGlobalForwarderAddedOrDisabled_ChangesTheNextQuery()
    {
        using FakeUdpUpstream upstream = FakeUdpUpstream.Start(
            BuildPositiveResponse(IPAddress.Parse("192.0.2.1"))
        );
        ForwarderQueryResolver resolver = CreateResolver();
        Assert.False(await IsHandledAsync(resolver, "example.com."));

        await SeedForwarderAsync($"127.0.0.1:{upstream.Port}");

        Assert.True(await IsHandledAsync(resolver, "example.com."));

        await DisableGlobalForwardersAsync();

        Assert.False(await IsHandledAsync(resolver, "example.com."));
        Assert.Equal(1, upstream.QueryCount);
    }

    /// <summary>
    ///     This test checks that a forwarder zone saved with a trailing dot and capitals (the
    ///     API stores the name as typed) matches queries for names under it.
    /// </summary>
    [Theory]
    [InlineData("corp.example.")]
    [InlineData("Corp.Example")]
    [InlineData("CORP.EXAMPLE.")]
    public async Task SetCache_AZoneNameStoredWithATrailingDotOrCapitals_StillMatches(
        string storedName
    )
    {
        using FakeUdpUpstream globalUpstream = FakeUdpUpstream.Start(
            BuildPositiveResponse(IPAddress.Parse("192.0.2.1"))
        );
        using FakeUdpUpstream zoneUpstream = FakeUdpUpstream.Start(
            BuildPositiveResponse(IPAddress.Parse("203.0.113.99"))
        );
        await SeedForwarderAsync($"127.0.0.1:{globalUpstream.Port}");
        await SeedForwarderZoneAsync(storedName, $"127.0.0.1:{zoneUpstream.Port}");
        ForwarderQueryResolver resolver = CreateResolver();

        Assert.True(await IsHandledAsync(resolver, "www.corp.example."));

        Assert.Equal(1, zoneUpstream.QueryCount);
        Assert.Equal(0, globalUpstream.QueryCount);
    }

    [Fact]
    public async Task SetCache_AForwardedResponseCacheHit_RunsNoDatabaseCommand()
    {
        using FakeUdpUpstream upstream = FakeUdpUpstream.Start(
            BuildPositiveResponse(IPAddress.Parse("192.0.2.10"))
        );
        await SeedForwarderAsync($"127.0.0.1:{upstream.Port}");
        ForwarderQueryResolver resolver = CreateCachingResolver(CreateResponseCache());

        // The first query loads the forwarder sets and fills the response cache.
        Assert.True(await IsHandledAsync(resolver, "www.example.com."));
        int commandsAfterFirst = _commands.Count;
        Assert.True(commandsAfterFirst > 0);

        for (int index = 0; index < 20; index++)
            Assert.True(await IsHandledAsync(resolver, "www.example.com."));

        Assert.Equal(1, upstream.QueryCount);
        Assert.Equal(commandsAfterFirst, _commands.Count);
    }
}
