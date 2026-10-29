using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.CloudSync;
using Ninlil.Dns.Server.Authoritative.Journal;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Extension;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.CloudSync;

/// <summary>
///     This class checks that <see cref="CloudDnsZoneSyncService" /> publishes
///     <see cref="CloudDnsZoneSyncFailedEvent" /> when a zone fails to sync. The failure is real: a
///     Cloudflare-style provider pointed at a closed loopback port, so the HTTP call is refused.
/// </summary>
public sealed class CloudDnsZoneSyncFailureEventTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-cloudsync-failure-event-tests-{Guid.NewGuid()}.sqlite3"
    );

    private IHost _host;
    private RecordingEventPublisher _publisher;
    private Guid _zoneId;

    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);
        builder.Services.AddNinlilDnsServerCore(builder.Configuration);

        // Overrides AddNinlilDnsServerCore's real IDnsServerEventPublisher registration --
        // this test only cares about capturing what gets published directly.
        _publisher = new RecordingEventPublisher();
        builder.Services.AddSingleton<IDnsServerEventPublisher>(_publisher);

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

        // A closed loopback port -- CloudflareDnsProvider's ListRecordsAsync HTTP call fails
        // immediately with a real connection-refused error, the same fast/deterministic-failure
        // technique AcmeCertificateRenewalServiceEventTests already establishes for a different
        // subsystem's new failure event.
        builder.Services.AddSingleton<ICloudDnsProvider>(
            new CloudflareDnsProvider(
                new HttpClient { BaseAddress = new Uri("http://127.0.0.1:1/") }
            )
        );
        builder.Services.AddSingleton<CloudDnsZoneSyncService>();

        _host = builder.Build();

        await using AsyncServiceScope seedScope = _host.Services.CreateAsyncScope();
        NinlilDnsContext context = seedScope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();

        IZoneRepository zoneRepository =
            seedScope.ServiceProvider.GetRequiredService<IZoneRepository>();
        ICloudDnsProviderCredentialRepository credentialRepository =
            seedScope.ServiceProvider.GetRequiredService<ICloudDnsProviderCredentialRepository>();
        IUnitOfWork unitOfWork = seedScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        CloudCredentialProtector protector =
            seedScope.ServiceProvider.GetRequiredService<CloudCredentialProtector>();

        Guid credentialId = Guid.NewGuid();
        await credentialRepository.AddAsync(
            new CloudDnsProviderCredentialEntity
            {
                Id = credentialId,
                Name = "fake-cloudflare",
                ProviderType = CloudDnsProviderTypeEnum.Cloudflare,
                CredentialsEncrypted = protector.Protect(
                    new Dictionary<string, string> { ["ApiToken"] = "fake-token" }
                ),
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );

        _zoneId = Guid.NewGuid();
        await zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = _zoneId,
                Name = "unreachable.example.com",
                Type = ZoneTypeEnum.CloudPrimary,
                CloudProviderCredentialId = credentialId,
                CloudProviderZoneId = "cf-zone-unreachable",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await unitOfWork.SaveChangesAsync();

        await _host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();

        TemporaryFiles.DeleteSqliteDatabase(_databasePath);
    }

    [Fact]
    public async Task ASyncFailureAgainstAnUnreachableProviderPublishesTheFailedEvent()
    {
        CloudDnsZoneSyncService syncService =
            _host.Services.GetRequiredService<CloudDnsZoneSyncService>();

        await syncService.StartAsync(CancellationToken.None);

        for (int attempt = 0; attempt < 100 && _publisher.Published.Count == 0; attempt++)
            await Task.Delay(100);

        await syncService.StopAsync(CancellationToken.None);

        CloudDnsZoneSyncFailedEvent failedEvent = Assert.Single(
            _publisher.Published.OfType<CloudDnsZoneSyncFailedEvent>()
        );
        Assert.Equal(_zoneId, failedEvent.ZoneId);
        Assert.Equal("unreachable.example.com", failedEvent.ZoneName);
        Assert.False(string.IsNullOrWhiteSpace(failedEvent.ErrorMessage));
    }
}
