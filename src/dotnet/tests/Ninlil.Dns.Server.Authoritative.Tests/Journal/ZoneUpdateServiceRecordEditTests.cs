using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Common.Enum;
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
///     This class tests <see cref="IZoneUpdateService.ApplyRecordEditAsync" />, used by the record
///     PUT and PATCH endpoints, against the EF Core repositories on a temporary SQLite database:
///     the record keeps its <see cref="EntityBase.Id" />, the journal records the before and after
///     versions, and an edit of the SOA itself is kept.
/// </summary>
public sealed class ZoneUpdateServiceRecordEditTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-record-edit-tests-{Guid.NewGuid()}.sqlite3"
    );

    private IHost _host;
    private Guid _zoneId;
    private Guid _aRecordId;
    private Guid _soaRecordId;
    private readonly List<DnsZoneChangedEvent> _publishedEvents = [];

    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);
        builder.Services.AddSingleton<IDnsServerEventPublisher>(
            new CapturingEventPublisher(_publishedEvents)
        );
        builder.Services.AddSingleton<IZoneWriteSerializer, ZoneWriteSerializer>();
        builder.Services.AddScoped<IZoneUpdateService, ZoneUpdateService>();

        _host = builder.Build();

        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        await SeedAsync(scope.ServiceProvider);
    }

    public Task DisposeAsync()
    {
        _host.Dispose();

        TemporaryFiles.DeleteSqliteDatabase(_databasePath);

        return Task.CompletedTask;
    }

    [Fact]
    public async Task ApplyRecordEditAsync_KeepsTheRecordsOwnIdAndBumpsTheSoaSerial()
    {
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        IZoneUpdateService zoneUpdateService =
            scope.ServiceProvider.GetRequiredService<IZoneUpdateService>();

        await zoneUpdateService.ApplyRecordEditAsync(
            _zoneId,
            _aRecordId,
            record =>
            {
                record.DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                    new ARecordData(IPAddress.Parse("192.0.2.2"))
                );
                record.TimeToLiveSeconds = 600;
            }
        );

        await using AsyncServiceScope verifyScope = _host.Services.CreateAsyncScope();
        IRecordRepository recordRepository =
            verifyScope.ServiceProvider.GetRequiredService<IRecordRepository>();
        IRecordRepository soaLookupRepository = recordRepository;

        RecordEntity editedRecord = await recordRepository.GetByIdAsync(_aRecordId);
        Assert.NotNull(editedRecord);
        Assert.Equal(_aRecordId, editedRecord.Id);
        Assert.Equal(600u, editedRecord.TimeToLiveSeconds);

        ARecordData editedData = Assert.IsType<ARecordData>(
            RecordEntityCodec.DecodeTypedData(editedRecord)
        );
        Assert.Equal(IPAddress.Parse("192.0.2.2"), editedData.Address);

        IReadOnlyList<RecordEntity> soaRecords = await soaLookupRepository.FindByNameAsync(
            _zoneId,
            "edit.example.com",
            DnsResourceRecordTypeEnum.Soa
        );
        SoaRecordData soaData = Assert.IsType<SoaRecordData>(
            RecordEntityCodec.DecodeTypedData(soaRecords[0])
        );
        Assert.Equal(101u, soaData.Serial);

        IZoneChangeJournalRepository journalRepository =
            verifyScope.ServiceProvider.GetRequiredService<IZoneChangeJournalRepository>();
        IReadOnlyList<ZoneChangeJournalEntryEntity> journalEntries =
            await journalRepository.ListSinceSerialAsync(_zoneId, 100);

        ZoneChangeJournalEntryEntity entry = Assert.Single(journalEntries);
        Assert.Equal(100L, entry.OldSerial);
        Assert.Equal(101L, entry.NewSerial);

        // The journal's before/after snapshot must reflect the record's real content change
        // -- not an empty diff with only the serial bump to show for it, which would leave
        // ZoneTransferService's IXFR responder unable to serve an accurate incremental
        // transfer across this exact change.
        Assert.Contains("192.0.2.1", entry.RemovedRecordsJson);
        Assert.Contains("192.0.2.2", entry.AddedRecordsJson);

        Assert.Single(_publishedEvents);
        Assert.Equal(_zoneId, _publishedEvents[0].ZoneId);
    }

    [Fact]
    public async Task ApplyRecordEditAsync_ThrowsWhenTheRecordDoesNotExist()
    {
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        IZoneUpdateService zoneUpdateService =
            scope.ServiceProvider.GetRequiredService<IZoneUpdateService>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            zoneUpdateService.ApplyRecordEditAsync(_zoneId, Guid.NewGuid(), _ => { })
        );
    }

    /// <summary>
    ///     This test checks that an edit adding, removing or moving the SOA (RFC 1034 §4.2.1) is
    ///     refused with the "An edit cannot change" message and saves nothing: the stored records
    ///     and the SOA serial stay as they were.
    /// </summary>
    [Theory]
    [InlineData("AToSoa")]
    [InlineData("SoaToA")]
    [InlineData("SoaRenamed")]
    public async Task ApplyRecordEditAsync_RefusesAnEditThatAddsRemovesOrMovesTheSoa(string edit)
    {
        await using (AsyncServiceScope scope = _host.Services.CreateAsyncScope())
        {
            IZoneUpdateService zoneUpdateService =
                scope.ServiceProvider.GetRequiredService<IZoneUpdateService>();

            Guid recordId = edit == "AToSoa" ? _aRecordId : _soaRecordId;
            InvalidOperationException exception =
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    zoneUpdateService.ApplyRecordEditAsync(
                        _zoneId,
                        recordId,
                        record =>
                        {
                            if (edit == "AToSoa")
                                record.Type = DnsResourceRecordTypeEnum.Soa;
                            else if (edit == "SoaToA")
                                record.Type = DnsResourceRecordTypeEnum.A;
                            else
                                record.Name = "moved.edit.example.com";
                        }
                    )
                );
            Assert.StartsWith("An edit cannot change", exception.Message);
        }

        await using AsyncServiceScope verifyScope = _host.Services.CreateAsyncScope();
        IRecordRepository recordRepository =
            verifyScope.ServiceProvider.GetRequiredService<IRecordRepository>();

        Assert.Equal(
            DnsResourceRecordTypeEnum.A,
            (await recordRepository.GetByIdAsync(_aRecordId)).Type
        );
        RecordEntity soa = await recordRepository.GetByIdAsync(_soaRecordId);
        Assert.Equal(DnsResourceRecordTypeEnum.Soa, soa.Type);
        Assert.Equal("edit.example.com", soa.Name);
        Assert.Equal(
            100u,
            Assert.IsType<SoaRecordData>(RecordEntityCodec.DecodeTypedData(soa)).Serial
        );
        Assert.Empty(_publishedEvents);
    }

    /// <summary>
    ///     This test edits the zone's SOA record itself through
    ///     <see cref="IZoneUpdateService.ApplyRecordEditAsync" />, which
    ///     <c>RecordsController.Replace</c> and <c>Patch</c> support. The edit must be saved even though
    ///     the SOA row is loaded twice (see ZoneUpdateService), and the SOA must not be journaled as
    ///     added/removed content, which would put extra SOA records inside an IXFR difference
    ///     sequence.
    /// </summary>
    [Fact]
    public async Task ApplyRecordEditAsync_EditingTheSoaRecordItselfPersistsTheEditAndJournalsNoContent()
    {
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        IZoneUpdateService zoneUpdateService =
            scope.ServiceProvider.GetRequiredService<IZoneUpdateService>();

        await zoneUpdateService.ApplyRecordEditAsync(
            _zoneId,
            _soaRecordId,
            record =>
            {
                SoaRecordData current = Assert.IsType<SoaRecordData>(
                    RecordEntityCodec.DecodeTypedData(record)
                );
                record.DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                    current with
                    {
                        RefreshSeconds = 7200,
                    }
                );
            }
        );

        await using AsyncServiceScope verifyScope = _host.Services.CreateAsyncScope();
        IRecordRepository recordRepository =
            verifyScope.ServiceProvider.GetRequiredService<IRecordRepository>();

        RecordEntity persistedSoa = await recordRepository.GetByIdAsync(_soaRecordId);
        SoaRecordData persistedSoaData = Assert.IsType<SoaRecordData>(
            RecordEntityCodec.DecodeTypedData(persistedSoa)
        );

        // The real field edit survived...
        Assert.Equal(7200u, persistedSoaData.RefreshSeconds);
        // ...alongside the serial bump ApplyRecordEditAsync always applies, not silently reverted
        // to the pre-edit content the way the original race would have left it.
        Assert.Equal(101u, persistedSoaData.Serial);

        IZoneChangeJournalRepository journalRepository =
            verifyScope.ServiceProvider.GetRequiredService<IZoneChangeJournalRepository>();
        IReadOnlyList<ZoneChangeJournalEntryEntity> journalEntries =
            await journalRepository.ListSinceSerialAsync(_zoneId, 100);

        ZoneChangeJournalEntryEntity entry = Assert.Single(journalEntries);
        Assert.Equal(100L, entry.OldSerial);
        Assert.Equal(101L, entry.NewSerial);

        // The SOA itself must never appear as ordinary added/removed diff content -- its own
        // identity crosses to a secondary purely through OldSerial/NewSerial.
        Assert.Equal("[]", entry.AddedRecordsJson);
        Assert.Equal("[]", entry.RemovedRecordsJson);
    }

    /// <summary>
    ///     This test covers <see cref="IZoneUpdateService.BumpSerialForDnssecResignAsync" /> --
    ///     the path a DNSSEC zone's proactive re-sign advances the SOA serial through with no
    ///     accompanying record content change at all.
    /// </summary>
    [Fact]
    public async Task BumpSerialForDnssecResignAsync_AdvancesTheSerialWithNoJournaledContent()
    {
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        IZoneUpdateService zoneUpdateService =
            scope.ServiceProvider.GetRequiredService<IZoneUpdateService>();

        await zoneUpdateService.BumpSerialForDnssecResignAsync(_zoneId);

        await using AsyncServiceScope verifyScope = _host.Services.CreateAsyncScope();
        IRecordRepository recordRepository =
            verifyScope.ServiceProvider.GetRequiredService<IRecordRepository>();

        RecordEntity persistedSoa = await recordRepository.GetByIdAsync(_soaRecordId);
        SoaRecordData persistedSoaData = Assert.IsType<SoaRecordData>(
            RecordEntityCodec.DecodeTypedData(persistedSoa)
        );
        Assert.Equal(101u, persistedSoaData.Serial);

        IZoneChangeJournalRepository journalRepository =
            verifyScope.ServiceProvider.GetRequiredService<IZoneChangeJournalRepository>();
        IReadOnlyList<ZoneChangeJournalEntryEntity> journalEntries =
            await journalRepository.ListSinceSerialAsync(_zoneId, 100);

        ZoneChangeJournalEntryEntity entry = Assert.Single(journalEntries);
        Assert.Equal(100L, entry.OldSerial);
        Assert.Equal(101L, entry.NewSerial);
        Assert.Equal("[]", entry.AddedRecordsJson);
        Assert.Equal("[]", entry.RemovedRecordsJson);

        Assert.Single(_publishedEvents);
        Assert.Equal(_zoneId, _publishedEvents[0].ZoneId);
    }

    private async Task SeedAsync(IServiceProvider services)
    {
        Data.EntityFrameworkCore.NinlilDnsContext context =
            services.GetRequiredService<Data.EntityFrameworkCore.NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();

        IZoneRepository zoneRepository = services.GetRequiredService<IZoneRepository>();
        IRecordRepository recordRepository = services.GetRequiredService<IRecordRepository>();
        IUnitOfWork unitOfWork = services.GetRequiredService<IUnitOfWork>();

        _zoneId = Guid.NewGuid();
        _aRecordId = Guid.NewGuid();
        _soaRecordId = Guid.NewGuid();

        await zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = _zoneId,
                Name = "edit.example.com",
                Type = ZoneTypeEnum.Primary,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );

        await recordRepository.AddAsync(
            new RecordEntity
            {
                Id = _soaRecordId,
                ZoneId = _zoneId,
                Name = "edit.example.com",
                Type = DnsResourceRecordTypeEnum.Soa,
                Class = DnsClassEnum.Internet,
                TimeToLiveSeconds = 3600,
                DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                    new SoaRecordData(
                        Common.Wire.DnsName.Parse("ns1.edit.example.com"),
                        Common.Wire.DnsName.Parse("admin.edit.example.com"),
                        100,
                        3600,
                        600,
                        604800,
                        300
                    )
                ),
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );

        await recordRepository.AddAsync(
            new RecordEntity
            {
                Id = _aRecordId,
                ZoneId = _zoneId,
                Name = "www.edit.example.com",
                Type = DnsResourceRecordTypeEnum.A,
                Class = DnsClassEnum.Internet,
                TimeToLiveSeconds = 300,
                DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                    new ARecordData(IPAddress.Parse("192.0.2.1"))
                ),
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );

        await unitOfWork.SaveChangesAsync();
    }

    /// <summary>
    ///     This class captures every <see cref="DnsZoneChangedEvent" /> published during a test,
    ///     standing in for the real channel-backed <c>DnsServerEventPublisher</c> so this test
    ///     does not need the full background dispatcher running to observe that a change was
    ///     reported.
    /// </summary>
    private sealed class CapturingEventPublisher(List<DnsZoneChangedEvent> events)
        : IDnsServerEventPublisher
    {
        public void Publish(IDnsServerEvent domainEvent)
        {
            if (domainEvent is DnsZoneChangedEvent zoneChangedEvent)
                events.Add(zoneChangedEvent);
        }
    }
}
