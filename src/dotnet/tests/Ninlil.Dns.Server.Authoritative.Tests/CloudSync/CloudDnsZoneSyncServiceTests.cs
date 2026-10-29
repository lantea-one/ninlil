using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.CloudSync;
using Ninlil.Dns.Server.Authoritative.Journal;
using Ninlil.Dns.Server.Authoritative.Resolution;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Extension;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.CloudSync;

/// <summary>
///     This class tests <see cref="CloudDnsZoneSyncService" /> end to end in both directions,
///     against Kestrel-hosted fake servers for Cloudflare (push, <see cref="ZoneTypeEnum.CloudPrimary" />)
///     and DigitalOcean (pull, <see cref="ZoneTypeEnum.CloudSecondary" />), with the EF Core
///     repositories on a temporary SQLite database. It also checks that
///     <see cref="AuthoritativeZoneCache" /> loads cloud zones, so
///     <see cref="AuthoritativeQueryResolver" /> answers from the synced records.
/// </summary>
public sealed class CloudDnsZoneSyncServiceTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-cloudsync-tests-{Guid.NewGuid()}.sqlite3"
    );

    private WebApplication _fakeCloudflare;
    private WebApplication _fakeDigitalOcean;
    private readonly List<object> _cloudflareCreateRequests = [];
    private readonly List<string> _cloudflareDeletedIds = [];

    private IHost _host;
    private AuthoritativeZoneCache _cache;
    private Guid _pushZoneId;
    private Guid _pullZoneId;

    public async Task InitializeAsync()
    {
        await StartFakeCloudflareAsync();
        await StartFakeDigitalOceanAsync();

        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);
        builder.Services.AddNinlilDnsServerCore(builder.Configuration);

        builder.Services.AddSingleton<IDataProtectionProvider>(
            new EphemeralDataProtectionProvider()
        );
        builder.Services.AddSingleton<CloudCredentialProtector>();
        builder.Services.AddSingleton<IZoneWriteSerializer, ZoneWriteSerializer>();
        builder.Services.AddScoped<IZoneUpdateService, ZoneUpdateService>();
        builder.Services.AddSingleton<AuthoritativeZoneCache>();
        builder.Services.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<AuthoritativeZoneCache>()
        );
        builder.Services.AddScoped<
            IDnsServerEventSubscriber<DnsZoneChangedEvent>,
            ZoneChangedEventSubscriber
        >();

        builder.Services.AddSingleton<ICloudDnsProvider>(
            new CloudflareDnsProvider(
                new HttpClient { BaseAddress = new Uri(_fakeCloudflare.Urls.First()) }
            )
        );
        builder.Services.AddSingleton<ICloudDnsProvider>(
            new DigitalOceanDnsProvider(
                new HttpClient { BaseAddress = new Uri(_fakeDigitalOcean.Urls.First()) }
            )
        );
        builder.Services.AddSingleton<CloudDnsZoneSyncService>();

        _host = builder.Build();

        await using AsyncServiceScope seedScope = _host.Services.CreateAsyncScope();
        await SeedAsync(seedScope.ServiceProvider);

        await _host.StartAsync();

        _cache = _host.Services.GetRequiredService<AuthoritativeZoneCache>();
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();

        await _fakeCloudflare.StopAsync();
        await _fakeCloudflare.DisposeAsync();
        await _fakeDigitalOcean.StopAsync();
        await _fakeDigitalOcean.DisposeAsync();

        TemporaryFiles.DeleteSqliteDatabase(_databasePath);
    }

    private async Task StartFakeCloudflareAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        _fakeCloudflare = builder.Build();

        _fakeCloudflare.MapGet(
            "/zones/{zoneId}/dns_records",
            () =>
                Results.Json(
                    new
                    {
                        result = new object[]
                        {
                            // A record with no matching local counterpart -- must be deleted.
                            new
                            {
                                id = "stale-record",
                                type = "A",
                                name = "stale.push.example.com",
                                content = "192.0.2.99",
                                ttl = 300,
                                priority = (int?)null,
                            },
                            // The provider's own apex NS record -- the sync must leave it alone.
                            new
                            {
                                id = "provider-apex-ns",
                                type = "NS",
                                name = "push.example.com",
                                content = "ns1.cloudflare.example",
                                ttl = 300,
                                priority = (int?)null,
                            },
                        },
                        result_info = new { total_pages = 1 },
                    }
                )
        );

        _fakeCloudflare.MapPost(
            "/zones/{zoneId}/dns_records",
            async (HttpContext context) =>
            {
                _cloudflareCreateRequests.Add(await context.Request.ReadFromJsonAsync<object>());
                return Results.Json(new { success = true });
            }
        );

        _fakeCloudflare.MapDelete(
            "/zones/{zoneId}/dns_records/{recordId}",
            (string recordId) =>
            {
                _cloudflareDeletedIds.Add(recordId);
                return Results.Json(new { success = true });
            }
        );

        await _fakeCloudflare.StartAsync();
    }

    private async Task StartFakeDigitalOceanAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        _fakeDigitalOcean = builder.Build();

        _fakeDigitalOcean.MapGet(
            "/domains/{domain}/records",
            () =>
                Results.Json(
                    new
                    {
                        domain_records = new object[]
                        {
                            // A record that only exists remotely -- must be pulled in locally.
                            new
                            {
                                id = 5001,
                                type = "A",
                                name = "remote-only",
                                data = "192.0.2.77",
                                ttl = 300,
                                priority = (int?)null,
                            },
                            // The provider's own apex NS records -- the sync must not add them locally.
                            new
                            {
                                id = 5002,
                                type = "NS",
                                name = "@",
                                data = "ns1.digitalocean.example.",
                                ttl = 1800,
                                priority = (int?)null,
                            },
                            new
                            {
                                id = 5003,
                                type = "NS",
                                name = "@",
                                data = "ns2.digitalocean.example.",
                                ttl = 1800,
                                priority = (int?)null,
                            },
                            // A delegation below the apex -- an ordinary record that must be pulled in.
                            new
                            {
                                id = 5004,
                                type = "NS",
                                name = "sub",
                                data = "ns.sub.example.net.",
                                ttl = 300,
                                priority = (int?)null,
                            },
                        },
                        links = new { pages = (object)null },
                    }
                )
        );

        await _fakeDigitalOcean.StartAsync();
    }

    private async Task SeedAsync(IServiceProvider services)
    {
        NinlilDnsContext context = services.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();

        IZoneRepository zoneRepository = services.GetRequiredService<IZoneRepository>();
        IRecordRepository recordRepository = services.GetRequiredService<IRecordRepository>();
        ICloudDnsProviderCredentialRepository credentialRepository =
            services.GetRequiredService<ICloudDnsProviderCredentialRepository>();
        IUnitOfWork unitOfWork = services.GetRequiredService<IUnitOfWork>();
        CloudCredentialProtector protector =
            services.GetRequiredService<CloudCredentialProtector>();

        Guid cloudflareCredentialId = Guid.NewGuid();
        Guid digitalOceanCredentialId = Guid.NewGuid();

        await credentialRepository.AddAsync(
            new CloudDnsProviderCredentialEntity
            {
                Id = cloudflareCredentialId,
                Name = "fake-cloudflare",
                ProviderType = CloudDnsProviderTypeEnum.Cloudflare,
                CredentialsEncrypted = protector.Protect(
                    new Dictionary<string, string> { ["ApiToken"] = "fake-token" }
                ),
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await credentialRepository.AddAsync(
            new CloudDnsProviderCredentialEntity
            {
                Id = digitalOceanCredentialId,
                Name = "fake-digitalocean",
                ProviderType = CloudDnsProviderTypeEnum.DigitalOcean,
                CredentialsEncrypted = protector.Protect(
                    new Dictionary<string, string> { ["ApiToken"] = "fake-token" }
                ),
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );

        _pushZoneId = Guid.NewGuid();
        _pullZoneId = Guid.NewGuid();

        await zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = _pushZoneId,
                Name = "push.example.com",
                Type = ZoneTypeEnum.CloudPrimary,
                CloudProviderCredentialId = cloudflareCredentialId,
                CloudProviderZoneId = "cf-zone-1",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = _pullZoneId,
                Name = "pull.example.com",
                Type = ZoneTypeEnum.CloudSecondary,
                CloudProviderCredentialId = digitalOceanCredentialId,
                CloudProviderZoneId = "pull.example.com",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await unitOfWork.SaveChangesAsync();

        async Task AddRecordAsync<TData>(
            Guid zoneId,
            string name,
            DnsResourceRecordTypeEnum type,
            TData data,
            bool excludeFromSync = false
        )
            where TData : IDnsRecordData =>
            await recordRepository.AddAsync(
                new RecordEntity
                {
                    Id = Guid.NewGuid(),
                    ZoneId = zoneId,
                    Name = name,
                    Type = type,
                    Class = DnsClassEnum.Internet,
                    TimeToLiveSeconds = 300,
                    DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                        typeof(TData),
                        data
                    ),
                    ExcludeFromSync = excludeFromSync,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                }
            );

        // The push zone's local content: its SOA (never synced, per ICloudDnsProvider's
        // own scoping), one record that must be pushed to Cloudflare (missing there today), and
        // one ExcludeFromSync record that must never be pushed at all.
        await AddRecordAsync(
            _pushZoneId,
            "push.example.com",
            DnsResourceRecordTypeEnum.Soa,
            new SoaRecordData(
                DnsName.Parse("ns1.push.example.com"),
                DnsName.Parse("admin.push.example.com"),
                1,
                3600,
                900,
                604800,
                3600
            )
        );
        await AddRecordAsync(
            _pushZoneId,
            "www.push.example.com",
            DnsResourceRecordTypeEnum.A,
            new ARecordData(System.Net.IPAddress.Parse("192.0.2.10"))
        );
        await AddRecordAsync(
            _pushZoneId,
            "sub.push.example.com",
            DnsResourceRecordTypeEnum.NS,
            new NsRecordData(DnsName.Parse("ns.sub.example.net"))
        );
        await AddRecordAsync(
            _pushZoneId,
            "local-only.push.example.com",
            DnsResourceRecordTypeEnum.A,
            new ARecordData(System.Net.IPAddress.Parse("192.0.2.11")),
            excludeFromSync: true
        );

        // The pull zone's local content: its SOA (needed so ApplyChangesAsync has
        // something to advance -- see CloudDnsZoneSyncService.SyncZoneAsync's bootstrap
        // note), one stale local record the remote no longer has (must be removed), and one
        // ExcludeFromSync record the remote also doesn't have but which must survive regardless.
        await AddRecordAsync(
            _pullZoneId,
            "pull.example.com",
            DnsResourceRecordTypeEnum.Soa,
            new SoaRecordData(
                DnsName.Parse("ns1.pull.example.com"),
                DnsName.Parse("admin.pull.example.com"),
                1,
                3600,
                900,
                604800,
                3600
            )
        );
        await AddRecordAsync(
            _pullZoneId,
            "stale-local.pull.example.com",
            DnsResourceRecordTypeEnum.A,
            new ARecordData(System.Net.IPAddress.Parse("192.0.2.66"))
        );
        await AddRecordAsync(
            _pullZoneId,
            "local-only.pull.example.com",
            DnsResourceRecordTypeEnum.A,
            new ARecordData(System.Net.IPAddress.Parse("192.0.2.67")),
            excludeFromSync: true
        );

        await unitOfWork.SaveChangesAsync();
    }

    /// <summary>
    ///     This method runs one sync pass over every cloud zone and waits for it to settle.
    /// </summary>
    /// <param name="completed">An optional condition that tells the pass has landed.</param>
    /// <param name="freshService">
    ///     When <see langword="true" />, the pass runs on a new service instance, because a stopped
    ///     <see cref="BackgroundService" /> cannot be started again (its token stays cancelled).
    /// </param>
    private async Task RunOneSyncPassAsync(Func<bool> completed = null, bool freshService = false)
    {
        CloudDnsZoneSyncService syncService = freshService
            ? ActivatorUtilities.CreateInstance<CloudDnsZoneSyncService>(_host.Services)
            : _host.Services.GetRequiredService<CloudDnsZoneSyncService>();

        await syncService.StartAsync(CancellationToken.None);

        // BackgroundService.StartAsync returns before ExecuteAsync has run its first pass (as for
        // DnscryptCertificateRotationService), so poll for the result instead of assuming it.
        for (int attempt = 0; attempt < 40 && _cloudflareCreateRequests.Count == 0; attempt++)
            await Task.Delay(50);

        // Give the pull-direction ApplyChangesAsync + its DnsZoneChangedEvent + the cache reload
        // it triggers a moment to land, on top of the create-request signal above. A
        // caller that knows what "landed" looks like waits for that instead (bounded),
        // since stopping the service any earlier would cancel a pass still in progress on a busy
        // machine.
        if (completed is null)
            await Task.Delay(500);
        else
            for (int attempt = 0; attempt < 300 && !completed(); attempt++)
                await Task.Delay(50);

        await syncService.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task CloudPrimaryZonePushesTheMissingLocalRecordToTheProvider()
    {
        await RunOneSyncPassAsync();

        Assert.Single(_cloudflareCreateRequests, request => request.ToString().Contains("www"));
    }

    /// <summary>
    ///     This test checks that a CloudPrimary pass does not delete the NS record the provider lists
    ///     at the zone apex (Route 53, for one, refuses that delete and fails the pass).
    /// </summary>
    [Fact]
    public async Task CloudPrimaryZoneLeavesTheProvidersApexNsRecordAlone()
    {
        await RunOneSyncPassAsync();

        Assert.Contains("stale-record", _cloudflareDeletedIds);
        Assert.DoesNotContain("provider-apex-ns", _cloudflareDeletedIds);
    }

    /// <summary>
    ///     This test is the guard that an NS record below the apex (a delegation) is still pushed.
    /// </summary>
    [Fact]
    public async Task CloudPrimaryZoneStillPushesALocalDelegation()
    {
        await RunOneSyncPassAsync();

        Assert.Single(_cloudflareCreateRequests, request => request.ToString().Contains("sub."));
    }

    /// <summary>
    ///     This test checks that repeated CloudSecondary passes do not add the provider's apex NS
    ///     records to the local zone, so the local apex NS rows stay as they were (none here).
    /// </summary>
    [Fact]
    public async Task CloudSecondaryZoneDoesNotAddTheProvidersApexNsRecordsOnAnyPass()
    {
        await RunOneSyncPassAsync();
        await RunOneSyncPassAsync(freshService: true);

        using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        IRecordRepository recordRepository =
            scope.ServiceProvider.GetRequiredService<IRecordRepository>();
        IReadOnlyList<RecordEntity> records = await recordRepository.ListByZoneIdAsync(_pullZoneId);

        Assert.Equal(
            0,
            records.Count(r =>
                r.Type == DnsResourceRecordTypeEnum.NS && r.Name == "pull.example.com"
            )
        );
    }

    /// <summary>
    ///     This test is the guard that an NS record below the apex (a delegation) is still pulled in,
    ///     once, however many passes run.
    /// </summary>
    [Fact]
    public async Task CloudSecondaryZoneStillPullsInARemoteDelegationOnce()
    {
        await RunOneSyncPassAsync();
        await RunOneSyncPassAsync(freshService: true);

        using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        IRecordRepository recordRepository =
            scope.ServiceProvider.GetRequiredService<IRecordRepository>();
        IReadOnlyList<RecordEntity> records = await recordRepository.ListByZoneIdAsync(_pullZoneId);

        Assert.Single(
            records,
            r => r.Type == DnsResourceRecordTypeEnum.NS && r.Name == "sub.pull.example.com"
        );
    }

    [Fact]
    public async Task CloudPrimaryZoneDeletesTheStaleRemoteRecord()
    {
        await RunOneSyncPassAsync();

        Assert.Contains("stale-record", _cloudflareDeletedIds);
    }

    [Fact]
    public async Task CloudPrimaryZoneNeverPushesAnExcludeFromSyncRecord()
    {
        await RunOneSyncPassAsync();

        Assert.DoesNotContain(
            _cloudflareCreateRequests,
            request => request.ToString().Contains("local-only")
        );
    }

    [Fact]
    public async Task CloudSecondaryZonePullsInTheRemoteOnlyRecord()
    {
        await RunOneSyncPassAsync();

        using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        IRecordRepository recordRepository =
            scope.ServiceProvider.GetRequiredService<IRecordRepository>();
        IReadOnlyList<RecordEntity> records = await recordRepository.ListByZoneIdAsync(_pullZoneId);

        Assert.Contains(records, r => r.Name == "remote-only.pull.example.com");
    }

    [Fact]
    public async Task CloudSecondaryZoneRemovesTheStaleLocalRecordThatIsNotExcludedFromSync()
    {
        await RunOneSyncPassAsync();

        using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        IRecordRepository recordRepository =
            scope.ServiceProvider.GetRequiredService<IRecordRepository>();
        IReadOnlyList<RecordEntity> records = await recordRepository.ListByZoneIdAsync(_pullZoneId);

        Assert.DoesNotContain(records, r => r.Name == "stale-local.pull.example.com");
    }

    [Fact]
    public async Task CloudSecondaryZoneKeepsAnExcludeFromSyncRecordEvenThoughTheRemoteDoesNotHaveIt()
    {
        await RunOneSyncPassAsync();

        using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        IRecordRepository recordRepository =
            scope.ServiceProvider.GetRequiredService<IRecordRepository>();
        IReadOnlyList<RecordEntity> records = await recordRepository.ListByZoneIdAsync(_pullZoneId);

        Assert.Contains(records, r => r.Name == "local-only.pull.example.com");
    }

    /// <summary>
    ///     This test checks that <see cref="AuthoritativeZoneCache" /> loads
    ///     <see cref="ZoneTypeEnum.CloudPrimary" /> and <see cref="ZoneTypeEnum.CloudSecondary" /> zones at
    ///     startup, so their synced records are answered; a type missing from the cache's filter would
    ///     be synced but never served.
    /// </summary>
    [Fact]
    public void BothCloudZoneTypesAreLoadedIntoTheCacheAtStartup()
    {
        CachedZone pushZone = _cache.FindZone(DnsName.Parse("push.example.com"));
        CachedZone pullZone = _cache.FindZone(DnsName.Parse("pull.example.com"));

        Assert.Equal(_pushZoneId, pushZone.Zone.Id);
        Assert.Equal(_pullZoneId, pullZone.Zone.Id);
    }

    [Fact]
    public async Task ACloudSecondaryZoneRemainsQueryableThroughTheCacheAfterASyncDrivenChange()
    {
        // The cache reloads the zone in the background, off the DnsZoneChangedEvent the sync
        // publishes, so the pass keeps running until the record shows up there (bounded).
        DnsName remoteOnly = DnsName.Parse("remote-only.pull.example.com");
        await RunOneSyncPassAsync(() =>
            _cache.FindZone(remoteOnly)?.NameExists(remoteOnly) ?? false
        );

        CachedZone pullZone = _cache.FindZone(remoteOnly);

        Assert.Equal(_pullZoneId, pullZone.Zone.Id);
        Assert.True(
            pullZone.NameExists(remoteOnly),
            "The newly pulled-in record should be visible through the cache, not just the database, once the sync-driven DnsZoneChangedEvent has been processed."
        );
    }
}
