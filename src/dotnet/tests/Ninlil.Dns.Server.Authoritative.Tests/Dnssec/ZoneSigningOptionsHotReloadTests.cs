using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Server.Authoritative.Dnssec;
using Ninlil.Dns.Server.Authoritative.Extension;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Extension;
using Ninlil.Dns.Server.Dnssec;
using Ninlil.Dns.Server.Dnssec.Extension;
using Ninlil.Dns.Server.Presets;
using Ninlil.Dns.Server.Tsig.Extension;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Dnssec;

/// <summary>
///     This class proves, against a real, already-running <see cref="SignedZoneCache" /> singleton
///     and a real <see cref="ZoneSigningOptions" /> configuration-file write -- not merely
///     <see cref="IOptionsMonitor{TOptions}.CurrentValue" /> read in isolation -- that
///     <see cref="ZoneSigningService" />'s own <see cref="ZoneSigningOptions.SignatureValidityPeriod" />
///     takes effect on the very next re-sign with no restart. Before this fix,
///     <see cref="ZoneSigningService" /> read <see cref="ZoneSigningOptions" /> via the usual,
///     snapshotted <see cref="IOptions{TOptions}" />, computed once and cached for the life of the
///     process -- a write reaching the configuration file would never have been observed at all,
///     even though <see cref="ZoneSigningService" /> is registered scoped and
///     <see cref="SignedZoneCache.ReloadZoneAsync" /> already constructs a brand-new scope (and
///     therefore a brand-new <see cref="ZoneSigningService" /> instance) on every single call --
///     the real, non-obvious finding here (see
///     <c>Ninlil.Web.Controllers.ZoneSigningSettingsController</c>'s own doc comment) is
///     that even a *fresh instance per call* does not help when the value it
///     reads through is itself a process-wide-cached <see cref="IOptions{TOptions}" />.
/// </summary>
public sealed class ZoneSigningOptionsHotReloadTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-zonesigning-hot-reload-{Guid.NewGuid()}.sqlite3"
    );

    private readonly string _configurationPath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-zonesigning-hot-reload-{Guid.NewGuid():N}.json"
    );

    private IHost _host;
    private Guid _zoneId;

    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();

        // A real IConfigurationRoot, backed by a real reload-on-change JSON file provider -- the
        // exact same kind of source WebApplicationBuilder.CreateBuilder() wires up for
        // appsettings.json in a real deployment.
        builder.Configuration.AddJsonFile(_configurationPath, optional: true, reloadOnChange: true);
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";

        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);
        builder.Services.AddNinlilDnsServerCore(builder.Configuration);
        builder.Services.AddSingleton<IDataProtectionProvider>(
            new EphemeralDataProtectionProvider()
        );
        builder.Services.AddNinlilDnsServerTsig(builder.Configuration);
        builder.Services.AddNinlilDnsServerDnssec();
        builder.Services.AddNinlilDnsServerAuthoritative(builder.Configuration);

        _host = builder.Build();

        await using AsyncServiceScope seedScope = _host.Services.CreateAsyncScope();
        NinlilDnsContext context = seedScope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();

        IZoneRepository zoneRepository =
            seedScope.ServiceProvider.GetRequiredService<IZoneRepository>();
        IRecordRepository recordRepository =
            seedScope.ServiceProvider.GetRequiredService<IRecordRepository>();
        IUnitOfWork unitOfWork = seedScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        IDnssecKeyManagementService keyManagement =
            seedScope.ServiceProvider.GetRequiredService<IDnssecKeyManagementService>();

        _zoneId = Guid.NewGuid();

        await zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = _zoneId,
                Name = "hot-reload.example.com",
                Type = ZoneTypeEnum.Primary,
                IsDnssecSigned = true,
                DnssecAlgorithm = DnssecAlgorithmEnum.Ed25519,
                DenialOfExistenceMode = DnssecDenialOfExistenceModeEnum.Nsec,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await unitOfWork.SaveChangesAsync();

        await recordRepository.AddAsync(
            new RecordEntity
            {
                Id = Guid.NewGuid(),
                ZoneId = _zoneId,
                Name = "hot-reload.example.com",
                Type = DnsResourceRecordTypeEnum.Soa,
                Class = DnsClassEnum.Internet,
                TimeToLiveSeconds = 3600,
                DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                    typeof(SoaRecordData),
                    new SoaRecordData(
                        DnsName.Parse("ns1.hot-reload.example.com"),
                        DnsName.Parse("admin.hot-reload.example.com"),
                        1,
                        3600,
                        900,
                        604800,
                        3600
                    )
                ),
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await unitOfWork.SaveChangesAsync();

        await keyManagement.GenerateKeyAsync(
            _zoneId,
            DnssecKeyRoleEnum.Zsk,
            DnssecAlgorithmEnum.Ed25519
        );
        await keyManagement.GenerateKeyAsync(
            _zoneId,
            DnssecKeyRoleEnum.Ksk,
            DnssecAlgorithmEnum.Ed25519
        );
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);

        if (File.Exists(_configurationPath))
            File.Delete(_configurationPath);
    }

    [Fact]
    public async Task AWriteThroughPresetConfigurationFileApplier_ChangesTheVeryNextReSignsExpiryWithNoRestart()
    {
        // SignedZoneCache.StartAsync runs the initial full sign synchronously as part of the
        // host's own startup, using this project's own compiled-in default: 30 days.
        await _host.StartAsync();

        SignedZoneCache cache = _host.Services.GetRequiredService<SignedZoneCache>();
        SignedZoneData before = cache.GetSignedData(_zoneId);
        Assert.NotNull(before);

        TimeSpan beforeValidity = before.SignatureExpiresAtUtc - DateTimeOffset.UtcNow;
        Assert.InRange(beforeValidity, TimeSpan.FromDays(29), TimeSpan.FromDays(30));

        IOptionsMonitor<ZoneSigningOptions> monitor = _host.Services.GetRequiredService<
            IOptionsMonitor<ZoneSigningOptions>
        >();
        TaskCompletionSource reloaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = monitor.OnChange(_ => reloaded.TrySetResult());

        // The exact same write mechanism ZoneSigningSettingsController.Patch itself calls --
        // shortening validity from the compiled-in 30 days down to 7.
        await PresetConfigurationFileApplier.ApplyAsync(
            [
                new PresetConfigurationSetting(
                    ZoneSigningOptions.SectionName,
                    nameof(ZoneSigningOptions.SignatureValidityPeriod),
                    TimeSpan.FromDays(7).ToString()
                ),
            ],
            _configurationPath
        );

        // .NET's own JSON configuration provider debounces file-change notifications by a few
        // hundred milliseconds -- this waits for that real reload to actually happen rather than
        // asserting against a fixed delay.
        Task completed = await Task.WhenAny(reloaded.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(reloaded.Task, completed);
        Assert.Equal(TimeSpan.FromDays(7), monitor.CurrentValue.SignatureValidityPeriod);

        // Re-signing now (SignedZoneCache constructs a brand-new scope, and therefore a brand-new
        // ZoneSigningService instance, for every ReloadZoneAsync call -- proving the fix must work
        // even though the class itself is never reused, only the configuration source is) must
        // reflect the new 7-day validity period, not the original 30-day one.
        await cache.ReloadZoneAsync(_zoneId);

        SignedZoneData after = cache.GetSignedData(_zoneId);
        TimeSpan afterValidity = after.SignatureExpiresAtUtc - DateTimeOffset.UtcNow;
        Assert.InRange(afterValidity, TimeSpan.FromDays(6), TimeSpan.FromDays(7));
    }
}
