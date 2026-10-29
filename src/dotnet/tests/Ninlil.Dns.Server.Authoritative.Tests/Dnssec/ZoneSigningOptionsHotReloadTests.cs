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
///     This class checks that a change to <see cref="ZoneSigningOptions.SignatureValidityPeriod" />
///     written to the configuration file reaches the next re-sign of a running
///     <see cref="SignedZoneCache" />, without a restart. <see cref="ZoneSigningService" /> must read
///     the options through <see cref="IOptionsMonitor{TOptions}" />: although it is created in a new
///     scope for every sign, an <see cref="IOptions{TOptions}" /> value is cached for the life of the
///     process (see <c>Ninlil.Web.Controllers.ZoneSigningSettingsController</c>).
/// </summary>
public sealed class ZoneSigningOptionsHotReloadTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-zonesigning-hot-reload-{Guid.NewGuid()}.sqlite3"
    );

    // A directory of its own rather than the shared temp directory: the file watcher behind
    // reloadOnChange reports every change in the directory it watches, and on a busy machine
    // unrelated churn there can overflow its queue and drop the change a test waits for. Only the
    // configuration file lives here; the database stays outside so its writes cannot disturb the
    // watcher.
    private readonly string _configurationDirectory = Directory
        .CreateTempSubdirectory("ninlil-dns-zonesigning-hot-reload-")
        .FullName;

    private string _configurationPath => Path.Combine(_configurationDirectory, "appsettings.json");

    private IHost _host;
    private Guid _zoneId;

    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();

        // A reload-on-change JSON file source, the same kind the web host uses for its
        // configuration files.
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

        if (Directory.Exists(_configurationDirectory))
            Directory.Delete(_configurationDirectory, recursive: true);
    }

    [Fact]
    public async Task AWriteThroughPresetConfigurationFileApplier_ChangesTheVeryNextReSignsExpiryWithNoRestart()
    {
        // SignedZoneCache.StartAsync runs the initial full sign synchronously as part of the
        // host's startup, using this project's compiled-in default: 30 days.
        await _host.StartAsync();

        SignedZoneCache cache = _host.Services.GetRequiredService<SignedZoneCache>();
        SignedZoneData before = cache.GetSignedData(_zoneId);
        Assert.NotNull(before);

        TimeSpan beforeValidity = before.SignatureExpiresAtUtc - DateTimeOffset.UtcNow;
        Assert.InRange(beforeValidity, TimeSpan.FromDays(29), TimeSpan.FromDays(30));

        IOptionsMonitor<ZoneSigningOptions> monitor = _host.Services.GetRequiredService<
            IOptionsMonitor<ZoneSigningOptions>
        >();

        // The same write ZoneSigningSettingsController.Patch makes, cutting validity from 30 days
        // to 7.
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

        // The JSON configuration provider debounces file-change notifications by a few hundred
        // milliseconds. Wait up to ten seconds for the written value itself, not for the first
        // change notification: a notification can arrive for an earlier state of the file and
        // reload before the final content is visible.
        for (
            int attempt = 0;
            attempt < 200
                && !(monitor.CurrentValue.SignatureValidityPeriod == TimeSpan.FromDays(7));
            attempt++
        )
            await Task.Delay(50);

        Assert.True(
            monitor.CurrentValue.SignatureValidityPeriod == TimeSpan.FromDays(7),
            "The configuration reload did not happen within the timeout."
        );
        Assert.Equal(TimeSpan.FromDays(7), monitor.CurrentValue.SignatureValidityPeriod);

        // ReloadZoneAsync creates a new scope and ZoneSigningService per call, so only the options
        // source carries the change; the new sign must use the 7-day validity, not 30 days.
        await cache.ReloadZoneAsync(_zoneId);

        SignedZoneData after = cache.GetSignedData(_zoneId);
        TimeSpan afterValidity = after.SignatureExpiresAtUtc - DateTimeOffset.UtcNow;
        Assert.InRange(afterValidity, TimeSpan.FromDays(6), TimeSpan.FromDays(7));
    }
}
