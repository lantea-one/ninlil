using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Server.Authoritative.Journal;
using Ninlil.Dns.Server.Authoritative.Options;
using Ninlil.Dns.Server.Authoritative.Ptr;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Presets;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Ptr;

/// <summary>
///     This class proves, against the real .NET configuration/options binding pipeline, a real
///     reload-on-change JSON configuration file, and a real, SQLite-backed
///     <see cref="IZoneUpdateService" />/repository stack -- not merely
///     <see cref="IOptionsMonitor{TOptions}.CurrentValue" /> read in isolation -- that a write
///     reaching <see cref="PtrSyncOptions.DefaultEnabled" /> via
///     <see cref="PresetConfigurationFileApplier" /> (the exact mechanism
///     <c>Ninlil.Web.Controllers.PtrSyncSettingsController.Patch</c> itself calls) is
///     honored by the <b>same, already-constructed</b> <see cref="PtrSyncService" /> instance on
///     its very next <see cref="PtrSyncService.ReconcileZoneAsync" /> call, with no restart. Before
///     this fix, <see cref="PtrSyncService" /> read <see cref="PtrSyncOptions" /> via the usual,
///     snapshotted <see cref="IOptions{TOptions}" />, computed once and cached for the life of the
///     process -- a write reaching the configuration file would never have been observed at all,
///     the same shape this project's other options-bound classes each already found and fixed for
///     their own options class.
/// </summary>
public sealed class PtrSyncOptionsHotReloadTests : IAsyncLifetime
{
    /// <summary>
    ///     This class stands in for the real, channel-backed <c>DnsServerEventPublisher</c> --
    ///     the same minimal double <c>PtrSyncServiceTests</c>' own identically-named, independently
    ///     duplicated fake already establishes: this test exercises <see cref="PtrSyncService" />
    ///     directly rather than through the event bus, so <see cref="IZoneUpdateService" />'s own
    ///     dependency on <see cref="IDnsServerEventPublisher" /> only needs a registration that
    ///     does not throw.
    /// </summary>
    private sealed class NoOpEventPublisher : IDnsServerEventPublisher
    {
        public void Publish(IDnsServerEvent domainEvent) { }
    }

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-ptr-sync-hot-reload-{Guid.NewGuid()}.sqlite3"
    );

    private readonly string _configurationPath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-ptr-sync-hot-reload-{Guid.NewGuid():N}.json"
    );

    private IHost _host;
    private IZoneRepository _zoneRepository;
    private IRecordRepository _recordRepository;
    private IUnitOfWork _unitOfWork;

    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();

        // A real IConfigurationRoot, backed by a real reload-on-change JSON file provider -- the
        // exact same kind of source WebApplicationBuilder.CreateBuilder() wires up for
        // appsettings.json in a real deployment.
        builder.Configuration.AddJsonFile(_configurationPath, optional: true, reloadOnChange: true);
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";

        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);
        builder.Services.AddSingleton<IDnsServerEventPublisher, NoOpEventPublisher>();
        builder.Services.AddSingleton<IZoneWriteSerializer, ZoneWriteSerializer>();
        builder.Services.AddScoped<IZoneUpdateService, ZoneUpdateService>();
        builder.Services.Configure<PtrSyncOptions>(
            builder.Configuration.GetSection(PtrSyncOptions.SectionName)
        );

        _host = builder.Build();

        await using AsyncServiceScope seedScope = _host.Services.CreateAsyncScope();
        NinlilDnsContext context = seedScope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();

        _zoneRepository = _host.Services.GetRequiredService<IZoneRepository>();
        _recordRepository = _host.Services.GetRequiredService<IRecordRepository>();
        _unitOfWork = _host.Services.GetRequiredService<IUnitOfWork>();
    }

    public Task DisposeAsync()
    {
        _host.Dispose();

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);

        if (File.Exists(_configurationPath))
            File.Delete(_configurationPath);

        return Task.CompletedTask;
    }

    [Fact]
    public async Task AWriteThroughPresetConfigurationFileApplier_MakesTheSameServiceInstanceStartSyncingWithNoRestart()
    {
        Guid zoneId = Guid.NewGuid();

        await _zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = zoneId,
                Name = "ptr-hot-reload.example.com",
                Type = ZoneTypeEnum.Primary,
                // Deliberately left unset -- this zone falls back to PtrSyncOptions'
                // own global default the whole way through this test.
                AutoManagePtrRecords = null,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await _unitOfWork.SaveChangesAsync();

        await _recordRepository.AddAsync(
            new RecordEntity
            {
                Id = Guid.NewGuid(),
                ZoneId = zoneId,
                Name = "ptr-hot-reload.example.com",
                Type = DnsResourceRecordTypeEnum.Soa,
                Class = DnsClassEnum.Internet,
                TimeToLiveSeconds = 3600,
                DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                    typeof(SoaRecordData),
                    new SoaRecordData(
                        DnsName.Parse("ns1.ptr-hot-reload.example.com"),
                        DnsName.Parse("admin.ptr-hot-reload.example.com"),
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
        await _unitOfWork.SaveChangesAsync();

        await _recordRepository.AddAsync(
            new RecordEntity
            {
                Id = Guid.NewGuid(),
                ZoneId = zoneId,
                Name = "host1.ptr-hot-reload.example.com",
                Type = DnsResourceRecordTypeEnum.A,
                Class = DnsClassEnum.Internet,
                TimeToLiveSeconds = 3600,
                DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                    typeof(ARecordData),
                    new ARecordData(IPAddress.Parse("192.0.2.77"))
                ),
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await _unitOfWork.SaveChangesAsync();

        IOptionsMonitor<PtrSyncOptions> monitor = _host.Services.GetRequiredService<
            IOptionsMonitor<PtrSyncOptions>
        >();

        // Before any write, the file does not exist yet -- this class's own compiled-in default:
        // off.
        Assert.False(monitor.CurrentValue.DefaultEnabled);

        // Constructed once, against a fresh dependency-injection scope's own repositories --
        // never rebuilt for the rest of this test.
        IServiceScope scope = _host.Services.CreateScope();
        PtrSyncService service = new(
            scope.ServiceProvider.GetRequiredService<IZoneRepository>(),
            scope.ServiceProvider.GetRequiredService<IRecordRepository>(),
            scope.ServiceProvider.GetRequiredService<IZoneUpdateService>(),
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
            monitor,
            NullLogger<PtrSyncService>.Instance
        );

        await service.ReconcileZoneAsync(zoneId);
        Assert.Null(await _zoneRepository.FindByNameAsync("2.0.192.in-addr.arpa"));

        TaskCompletionSource reloaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = monitor.OnChange(_ => reloaded.TrySetResult());

        // The exact same write mechanism PtrSyncSettingsController.Patch itself calls.
        await PresetConfigurationFileApplier.ApplyAsync(
            [
                new PresetConfigurationSetting(
                    PtrSyncOptions.SectionName,
                    nameof(PtrSyncOptions.DefaultEnabled),
                    true
                ),
            ],
            _configurationPath
        );

        // .NET's own JSON configuration provider debounces file-change notifications by a few
        // hundred milliseconds -- this waits for that real reload to actually happen rather than
        // asserting against a fixed delay.
        Task completed = await Task.WhenAny(reloaded.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(reloaded.Task, completed);
        Assert.True(monitor.CurrentValue.DefaultEnabled);

        // The identical service instance, constructed before the write, now genuinely reconciles
        // the reverse zone into existence -- proof the global default's own gate was actually
        // re-evaluated rather than the service simply always answering the same way regardless of
        // configuration.
        await service.ReconcileZoneAsync(zoneId);

        ZoneEntity reverseZone = await _zoneRepository.FindByNameAsync("2.0.192.in-addr.arpa");
        Assert.NotNull(reverseZone);
    }
}
