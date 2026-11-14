using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Server.Authoritative.Cache;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests;

/// <summary>
///     This class tests <see cref="AuthoritativeZoneCache.FindZone" /> against a cache loaded from
///     a temporary SQLite database: which zone answers for names at, below and beside a hosted
///     zone, and that a lookup allocates no bytes.
/// </summary>
public sealed class AuthoritativeZoneCacheFindZoneTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-findzone-tests-{Guid.NewGuid()}.sqlite3"
    );

    private IHost _host;
    private AuthoritativeZoneCache _cache;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);
        builder.Services.AddSingleton<AuthoritativeZoneCache>();

        _host = builder.Build();

        await using (AsyncServiceScope seedScope = _host.Services.CreateAsyncScope())
        {
            NinlilDnsContext context =
                seedScope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
            await context.Database.EnsureCreatedAsync();

            IZoneRepository zones = seedScope.ServiceProvider.GetRequiredService<IZoneRepository>();

            foreach (string name in new[] { "example.com", "sub.example.com", "badexample.com" })
                await zones.AddAsync(
                    new ZoneEntity
                    {
                        Id = Guid.NewGuid(),
                        Name = name,
                        Type = ZoneTypeEnum.Primary,
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                    }
                );

            await seedScope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        _cache = _host.Services.GetRequiredService<AuthoritativeZoneCache>();
        await _cache.StartAsync(CancellationToken.None);
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        _host.Dispose();

        // The helper clears this database's connection pool first: a pooled connection keeps the
        // file open, and Windows refuses to delete an open file.
        TemporaryFiles.DeleteSqliteDatabase(_databasePath);

        return Task.CompletedTask;
    }

    /// <summary>
    ///     This test checks the zone found for names at, below and beside the hosted zones, with
    ///     mixed case and a sibling that shares a suffix as text only.
    /// </summary>
    [Theory]
    [InlineData("example.com", "example.com")]
    [InlineData("EXAMPLE.Com", "example.com")]
    [InlineData("www.example.com", "example.com")]
    [InlineData("a.b.sub.example.com", "sub.example.com")]
    [InlineData("www.badexample.com", "badexample.com")]
    [InlineData("ample.com", null)]
    [InlineData("xexample.com", null)]
    [InlineData("com", null)]
    [InlineData(".", null)]
    public void FindZone_ForANameAroundTheHostedZones_ReturnsTheClosestEnclosingZone(
        string query,
        string expectedZone
    )
    {
        CachedZone zone = _cache.FindZone(DnsName.Parse(query));

        Assert.Equal(expectedZone, zone?.Zone.Name);
    }

    /// <summary>
    ///     This test checks that a lookup allocates no bytes, for a hit below a zone, an exact hit
    ///     and a miss.
    /// </summary>
    [Fact]
    public void FindZone_ForAHitAnExactHitAndAMiss_AllocatesNothing()
    {
        DnsName below = DnsName.Parse("www.sub.example.com");
        DnsName exact = DnsName.Parse("example.com");
        DnsName miss = DnsName.Parse("www.example.org");

        // Warm up so JIT work and the one-time build of each name's hash code are not counted.
        _cache.FindZone(below);
        _cache.FindZone(exact);
        _cache.FindZone(miss);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            _cache.FindZone(below);
            _cache.FindZone(exact);
            _cache.FindZone(miss);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
