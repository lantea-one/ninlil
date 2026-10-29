using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.Journal;
using Ninlil.Dns.Server.Core.Events;
using Xunit;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Journal;

/// <summary>
///     This class tests the SOA history that <see cref="ZoneUpdateService" /> writes into every
///     journal entry (<see cref="ZoneChangeJournalEntryEntity.OldSoaRecordJson" /> and
///     <see cref="ZoneChangeJournalEntryEntity.NewSoaRecordJson" />), one test per writer path,
///     against the EF Core repositories on a temporary SQLite database (the same host shape as
///     <see cref="ZoneUpdateServiceRecordEditTests" />). On EF the SOA row is a tracked object that
///     the service mutates, so these tests also guard that the old snapshot is taken before the
///     mutation.
/// </summary>
public sealed class ZoneUpdateServiceSoaHistoryTests : IAsyncLifetime
{
    private const string ZoneName = "history.example.com";

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-soa-history-tests-{Guid.NewGuid()}.sqlite3"
    );

    private IHost _host;
    private Guid _zoneId;
    private Guid _aRecordId;
    private Guid _soaRecordId;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);
        builder.Services.AddSingleton<IDnsServerEventPublisher>(new DiscardingEventPublisher());
        builder.Services.AddSingleton<IZoneWriteSerializer, ZoneWriteSerializer>();
        builder.Services.AddScoped<IZoneUpdateService, ZoneUpdateService>();

        _host = builder.Build();

        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        await SeedAsync(scope.ServiceProvider);
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        _host.Dispose();

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);

        return Task.CompletedTask;
    }

    private static SoaRecordData InitialSoa() =>
        new(
            DnsName.Parse("ns1.history.example.com"),
            DnsName.Parse("admin.history.example.com"),
            100,
            3600,
            600,
            604800,
            300
        );

    private static RecordEntity SoaEntity(Guid id, Guid zoneId, SoaRecordData data, uint ttl) =>
        new()
        {
            Id = id,
            ZoneId = zoneId,
            Name = ZoneName,
            Type = DnsResourceRecordTypeEnum.Soa,
            Class = DnsClassEnum.Internet,
            TimeToLiveSeconds = ttl,
            DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(data),
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

    private static RecordEntity ARecord(Guid zoneId, string address) =>
        new()
        {
            Id = Guid.NewGuid(),
            ZoneId = zoneId,
            Name = "host." + ZoneName,
            Type = DnsResourceRecordTypeEnum.A,
            Class = DnsClassEnum.Internet,
            TimeToLiveSeconds = 300,
            DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                new ARecordData(IPAddress.Parse(address))
            ),
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

    private static SoaRecordData SoaData(string snapshotJson) =>
        Assert.IsType<SoaRecordData>(
            RecordEntityCodec.DecodeTypedData(
                Ninlil.Common.Serializer.JsonSerializer.Deserialize<RecordEntity>(snapshotJson)
            )
        );

    private async Task<IReadOnlyList<ZoneChangeJournalEntryEntity>> ReadJournalAsync(Guid zoneId)
    {
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        IReadOnlyList<ZoneChangeJournalEntryEntity> entries = await scope
            .ServiceProvider.GetRequiredService<IZoneChangeJournalRepository>()
            .ListSinceSerialAsync(zoneId, 0);
        return [.. entries.OrderBy(entry => entry.NewSerial)];
    }

    private async Task<RecordEntity> ReadStoredSoaAsync()
    {
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<IRecordRepository>()
            .GetByIdAsync(_soaRecordId);
    }

    /// <summary>
    ///     A record change records the SOA before it (the seeded one) and the same SOA with the
    ///     serial advanced by one.
    /// </summary>
    [Fact]
    public async Task ApplyChangesAsync_RecordsTheOldSoaAndTheSameSoaWithTheNextSerial()
    {
        await using (AsyncServiceScope scope = _host.Services.CreateAsyncScope())
        {
            await scope
                .ServiceProvider.GetRequiredService<IZoneUpdateService>()
                .ApplyChangesAsync(_zoneId, [ARecord(_zoneId, "192.0.2.9")], []);
        }

        ZoneChangeJournalEntryEntity entry = Assert.Single(await ReadJournalAsync(_zoneId));
        RecordEntity oldSoa = Ninlil.Common.Serializer.JsonSerializer.Deserialize<RecordEntity>(
            entry.OldSoaRecordJson
        );
        RecordEntity newSoa = Ninlil.Common.Serializer.JsonSerializer.Deserialize<RecordEntity>(
            entry.NewSoaRecordJson
        );

        Assert.Equal(ZoneName, oldSoa.Name);
        Assert.Equal(3600u, oldSoa.TimeToLiveSeconds);
        Assert.Equal(InitialSoa(), SoaData(entry.OldSoaRecordJson));
        Assert.Equal(ZoneName, newSoa.Name);
        Assert.Equal(3600u, newSoa.TimeToLiveSeconds);
        Assert.Equal(InitialSoa() with { Serial = 101 }, SoaData(entry.NewSoaRecordJson));
    }

    /// <summary>
    ///     A change that carries the primary's SOA (an incoming IXFR step) records the old SOA as
    ///     stored and a new SOA with the replacement's fields, TTL and the explicit serial.
    /// </summary>
    [Fact]
    public async Task ApplyChangesAsync_WithAReplacementSoa_RecordsTheReplacementsFieldsAndTtl()
    {
        SoaRecordData replacement = InitialSoa() with
        {
            Serial = 150,
            RefreshSeconds = 1800,
            MinimumTtlSeconds = 60,
        };
        DnsResourceRecord replacementWire = RecordEntityCodec.ToWireRecord(
            SoaEntity(Guid.NewGuid(), _zoneId, replacement, 900),
            DnsName.Parse(ZoneName)
        );

        await using (AsyncServiceScope scope = _host.Services.CreateAsyncScope())
        {
            await scope
                .ServiceProvider.GetRequiredService<IZoneUpdateService>()
                .ApplyChangesAsync(
                    _zoneId,
                    [ARecord(_zoneId, "192.0.2.9")],
                    [],
                    explicitNewSerial: 150,
                    replacementSoa: replacementWire
                );
        }

        ZoneChangeJournalEntryEntity entry = Assert.Single(await ReadJournalAsync(_zoneId));
        Assert.Equal(100L, entry.OldSerial);
        Assert.Equal(150L, entry.NewSerial);
        Assert.Equal(InitialSoa(), SoaData(entry.OldSoaRecordJson));
        Assert.Equal(
            3600u,
            Ninlil
                .Common.Serializer.JsonSerializer.Deserialize<RecordEntity>(entry.OldSoaRecordJson)
                .TimeToLiveSeconds
        );
        Assert.Equal(replacement, SoaData(entry.NewSoaRecordJson));
        Assert.Equal(
            900u,
            Ninlil
                .Common.Serializer.JsonSerializer.Deserialize<RecordEntity>(entry.NewSoaRecordJson)
                .TimeToLiveSeconds
        );
    }

    /// <summary>
    ///     An in-place edit of an ordinary record records the SOA before and after the serial bump.
    /// </summary>
    [Fact]
    public async Task ApplyRecordEditAsync_OnARecord_RecordsTheOldAndTheBumpedSoa()
    {
        await using (AsyncServiceScope scope = _host.Services.CreateAsyncScope())
        {
            await scope
                .ServiceProvider.GetRequiredService<IZoneUpdateService>()
                .ApplyRecordEditAsync(_zoneId, _aRecordId, record => record.TimeToLiveSeconds = 60);
        }

        ZoneChangeJournalEntryEntity entry = Assert.Single(await ReadJournalAsync(_zoneId));
        Assert.Equal(InitialSoa(), SoaData(entry.OldSoaRecordJson));
        Assert.Equal(InitialSoa() with { Serial = 101 }, SoaData(entry.NewSoaRecordJson));
    }

    /// <summary>
    ///     An edit of the SOA itself (Refresh changes) records the pre-edit Refresh in the old SOA.
    ///     On EF the edited object and the loaded SOA are the same tracked instance, so this fails
    ///     if the old snapshot is taken after <c>applyEdit</c>. The new SOA carries the edit and
    ///     the bumped serial, and the added and removed lists stay empty.
    /// </summary>
    [Fact]
    public async Task ApplyRecordEditAsync_OnTheSoaItself_RecordsThePreEditSoaAsTheOldOne()
    {
        await using (AsyncServiceScope scope = _host.Services.CreateAsyncScope())
        {
            await scope
                .ServiceProvider.GetRequiredService<IZoneUpdateService>()
                .ApplyRecordEditAsync(
                    _zoneId,
                    _soaRecordId,
                    record =>
                        record.DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                            InitialSoa() with
                            {
                                RefreshSeconds = 7200,
                            }
                        )
                );
        }

        ZoneChangeJournalEntryEntity entry = Assert.Single(await ReadJournalAsync(_zoneId));
        Assert.Equal(3600u, SoaData(entry.OldSoaRecordJson).RefreshSeconds);
        Assert.Equal(100u, SoaData(entry.OldSoaRecordJson).Serial);
        Assert.Equal(7200u, SoaData(entry.NewSoaRecordJson).RefreshSeconds);
        Assert.Equal(101u, SoaData(entry.NewSoaRecordJson).Serial);
        Assert.Equal("[]", entry.AddedRecordsJson);
        Assert.Equal("[]", entry.RemovedRecordsJson);
    }

    /// <summary>
    ///     A DNSSEC re-sign bump records the SOA before it and the same SOA with the next serial.
    /// </summary>
    [Fact]
    public async Task BumpSerialForDnssecResignAsync_RecordsTheOldAndTheBumpedSoa()
    {
        await using (AsyncServiceScope scope = _host.Services.CreateAsyncScope())
        {
            await scope
                .ServiceProvider.GetRequiredService<IZoneUpdateService>()
                .BumpSerialForDnssecResignAsync(_zoneId);
        }

        ZoneChangeJournalEntryEntity entry = Assert.Single(await ReadJournalAsync(_zoneId));
        Assert.Equal(InitialSoa(), SoaData(entry.OldSoaRecordJson));
        Assert.Equal(InitialSoa() with { Serial = 101 }, SoaData(entry.NewSoaRecordJson));
    }

    /// <summary>
    ///     Bootstrapping an empty zone records no old SOA (there is no older version) and the
    ///     bootstrap SOA as the new one.
    /// </summary>
    [Fact]
    public async Task BootstrapZoneAsync_RecordsNoOldSoaAndTheBootstrapSoaAsTheNewOne()
    {
        Guid zoneId = Guid.NewGuid();
        await using (AsyncServiceScope scope = _host.Services.CreateAsyncScope())
        {
            await scope
                .ServiceProvider.GetRequiredService<IZoneRepository>()
                .AddAsync(
                    new ZoneEntity
                    {
                        Id = zoneId,
                        Name = ZoneName + ".bootstrap",
                        Type = ZoneTypeEnum.Primary,
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                    }
                );
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        SoaRecordData bootstrapSoa = InitialSoa() with { Serial = 7 };
        await using (AsyncServiceScope scope = _host.Services.CreateAsyncScope())
        {
            await scope
                .ServiceProvider.GetRequiredService<IZoneUpdateService>()
                .BootstrapZoneAsync(
                    zoneId,
                    [SoaEntity(Guid.NewGuid(), zoneId, bootstrapSoa, 1200)]
                );
        }

        ZoneChangeJournalEntryEntity entry = Assert.Single(await ReadJournalAsync(zoneId));
        Assert.Null(entry.OldSoaRecordJson);
        Assert.Equal(bootstrapSoa, SoaData(entry.NewSoaRecordJson));
        Assert.Equal(
            1200u,
            Ninlil
                .Common.Serializer.JsonSerializer.Deserialize<RecordEntity>(entry.NewSoaRecordJson)
                .TimeToLiveSeconds
        );
    }

    /// <summary>
    ///     Three changes in a row (a replacement SOA, a record change and an SOA edit) leave a
    ///     chain: each entry's old SOA equals the previous entry's new SOA, and the last new SOA
    ///     equals the stored SOA. IXFR needs this chain to be unbroken.
    /// </summary>
    [Fact]
    public async Task ThreeChanges_FormAChainThatEndsAtTheStoredSoa()
    {
        SoaRecordData replacement = InitialSoa() with { Serial = 110, RefreshSeconds = 1800 };
        DnsResourceRecord replacementWire = RecordEntityCodec.ToWireRecord(
            SoaEntity(Guid.NewGuid(), _zoneId, replacement, 900),
            DnsName.Parse(ZoneName)
        );

        await using (AsyncServiceScope scope = _host.Services.CreateAsyncScope())
        {
            IZoneUpdateService service =
                scope.ServiceProvider.GetRequiredService<IZoneUpdateService>();
            await service.ApplyChangesAsync(
                _zoneId,
                [ARecord(_zoneId, "192.0.2.9")],
                [],
                explicitNewSerial: 110,
                replacementSoa: replacementWire
            );
            await service.ApplyRecordEditAsync(
                _zoneId,
                _aRecordId,
                record => record.TimeToLiveSeconds = 60
            );
            await service.ApplyRecordEditAsync(
                _zoneId,
                _soaRecordId,
                record =>
                    record.DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                        replacement with
                        {
                            MinimumTtlSeconds = 42,
                        }
                    )
            );
        }

        IReadOnlyList<ZoneChangeJournalEntryEntity> entries = await ReadJournalAsync(_zoneId);
        Assert.Equal(3, entries.Count);
        Assert.Equal([100L, 110L, 111L], entries.Select(entry => entry.OldSerial));
        for (int index = 1; index < entries.Count; index++)
            Assert.Equal(entries[index - 1].NewSoaRecordJson, entries[index].OldSoaRecordJson);

        RecordEntity stored = await ReadStoredSoaAsync();
        SoaRecordData last = SoaData(entries[^1].NewSoaRecordJson);
        Assert.Equal(RecordEntityCodec.DecodeTypedData(stored), last);
        Assert.Equal(42u, last.MinimumTtlSeconds);
        Assert.Equal(112u, last.Serial);
    }

    private async Task SeedAsync(IServiceProvider services)
    {
        Data.EntityFrameworkCore.NinlilDnsContext context =
            services.GetRequiredService<Data.EntityFrameworkCore.NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();

        _zoneId = Guid.NewGuid();
        _aRecordId = Guid.NewGuid();
        _soaRecordId = Guid.NewGuid();

        await services
            .GetRequiredService<IZoneRepository>()
            .AddAsync(
                new ZoneEntity
                {
                    Id = _zoneId,
                    Name = ZoneName,
                    Type = ZoneTypeEnum.Primary,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                }
            );

        IRecordRepository recordRepository = services.GetRequiredService<IRecordRepository>();
        await recordRepository.AddAsync(SoaEntity(_soaRecordId, _zoneId, InitialSoa(), 3600));
        RecordEntity aRecord = ARecord(_zoneId, "192.0.2.1");
        aRecord.Id = _aRecordId;
        await recordRepository.AddAsync(aRecord);

        await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    /// <summary>
    ///     This class stands in for the channel-backed event publisher: these tests read the journal
    ///     and do not need the dispatcher running.
    /// </summary>
    private sealed class DiscardingEventPublisher : IDnsServerEventPublisher
    {
        /// <inheritdoc />
        public void Publish(IDnsServerEvent domainEvent) { }
    }
}
