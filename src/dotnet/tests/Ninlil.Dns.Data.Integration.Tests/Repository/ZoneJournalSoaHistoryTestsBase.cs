using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.Journal;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Repository;

/// <summary>
///     This class holds the tests of the SOA history that zone change journal entries carry
///     (<see cref="ZoneChangeJournalEntryEntity.OldSoaRecordJson" /> and
///     <see cref="ZoneChangeJournalEntryEntity.NewSoaRecordJson" />) that run against every engine:
///     one subclass per engine supplies the fixture and a fresh database.
/// </summary>
public abstract class ZoneJournalSoaHistoryTestsBase(EngineFixtureBase fixture)
{
    private const string OldSoaJson =
        "{\"Name\":\"example.com\",\"Type\":6,\"Class\":1,\"TimeToLiveSeconds\":3600,\"DataJson\":\"old\"}";

    private const string NewSoaJson =
        "{\"Name\":\"example.com\",\"Type\":6,\"Class\":1,\"TimeToLiveSeconds\":300,\"DataJson\":\"new\"}";

    private static ZoneChangeJournalEntryEntity Entry(
        Guid zoneId,
        long oldSerial,
        string oldSoaJson,
        string newSoaJson
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            ZoneId = zoneId,
            OldSerial = oldSerial,
            NewSerial = oldSerial + 1,
            RemovedRecordsJson = "[]",
            AddedRecordsJson = "[]",
            OldSoaRecordJson = oldSoaJson,
            NewSoaRecordJson = newSoaJson,
            CreatedAtUtc = new DateTimeOffset(2026, 10, 29, 8, 0, 0, TimeSpan.Zero),
        };

    /// <summary>
    ///     An entry with both SOA snapshots and an entry with neither come back from
    ///     <see cref="IZoneChangeJournalRepository.ListSinceSerialAsync" /> with exactly the text
    ///     that was stored, and null stays null (it is not turned into an empty string).
    /// </summary>
    [Fact]
    public async Task ListSinceSerialAsync_ReturnsTheStoredSoaSnapshotsAndKeepsNullsNull()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        Guid zoneId = Guid.NewGuid();

        await using (AsyncServiceScope writeScope = database.Services.CreateAsyncScope())
        {
            IZoneChangeJournalRepository writer =
                writeScope.ServiceProvider.GetRequiredService<IZoneChangeJournalRepository>();
            await writer.AddAsync(Entry(zoneId, 10, OldSoaJson, NewSoaJson));
            await writer.AddAsync(Entry(zoneId, 11, null, null));
            await writeScope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await using AsyncServiceScope readScope = database.Services.CreateAsyncScope();
        IReadOnlyList<ZoneChangeJournalEntryEntity> entries = await readScope
            .ServiceProvider.GetRequiredService<IZoneChangeJournalRepository>()
            .ListSinceSerialAsync(zoneId, 0);

        ZoneChangeJournalEntryEntity withHistory = Assert.Single(
            entries,
            entry => entry.OldSerial == 10
        );
        Assert.Equal(OldSoaJson, withHistory.OldSoaRecordJson);
        Assert.Equal(NewSoaJson, withHistory.NewSoaRecordJson);

        ZoneChangeJournalEntryEntity withoutHistory = Assert.Single(
            entries,
            entry => entry.OldSerial == 11
        );
        Assert.Null(withoutHistory.OldSoaRecordJson);
        Assert.Null(withoutHistory.NewSoaRecordJson);
    }

    /// <summary>
    ///     An edit of the SOA record itself, made through a real <see cref="ZoneUpdateService" />,
    ///     stores the pre-edit SOA as the old one on every engine. The engines load the SOA
    ///     differently (EF hands back the tracked object that the edit then changes, MongoDB a
    ///     separate copy), so each is checked.
    /// </summary>
    [Fact]
    public async Task ApplyRecordEditAsync_OnTheSoaItself_StoresThePreEditSoaAsTheOldOne()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        Guid zoneId = Guid.NewGuid();
        Guid soaId = Guid.NewGuid();
        SoaRecordData initial = new(
            DnsName.Parse("ns1.example.com"),
            DnsName.Parse("admin.example.com"),
            100,
            3600,
            600,
            604800,
            300
        );

        await using (AsyncServiceScope seedScope = database.Services.CreateAsyncScope())
        {
            await seedScope
                .ServiceProvider.GetRequiredService<IZoneRepository>()
                .AddAsync(
                    new ZoneEntity
                    {
                        Id = zoneId,
                        Name = "example.com",
                        Type = ZoneTypeEnum.Primary,
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                    }
                );
            await seedScope
                .ServiceProvider.GetRequiredService<IRecordRepository>()
                .AddAsync(
                    new RecordEntity
                    {
                        Id = soaId,
                        ZoneId = zoneId,
                        Name = "example.com",
                        Type = DnsResourceRecordTypeEnum.Soa,
                        Class = DnsClassEnum.Internet,
                        TimeToLiveSeconds = 3600,
                        DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(initial),
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                    }
                );
            await seedScope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await using (AsyncServiceScope editScope = database.Services.CreateAsyncScope())
        {
            IServiceProvider services = editScope.ServiceProvider;
            ZoneUpdateService service = new(
                services.GetRequiredService<IZoneRepository>(),
                services.GetRequiredService<IRecordRepository>(),
                services.GetRequiredService<IZoneChangeJournalRepository>(),
                services.GetRequiredService<IUnitOfWork>(),
                new DiscardingEventPublisher(),
                new ZoneWriteSerializer()
            );
            await service.ApplyRecordEditAsync(
                zoneId,
                soaId,
                record =>
                    record.DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                        initial with
                        {
                            RefreshSeconds = 7200,
                        }
                    )
            );
        }

        await using AsyncServiceScope readScope = database.Services.CreateAsyncScope();
        ZoneChangeJournalEntryEntity entry = Assert.Single(
            await readScope
                .ServiceProvider.GetRequiredService<IZoneChangeJournalRepository>()
                .ListSinceSerialAsync(zoneId, 0)
        );
        Assert.Equal(3600u, SoaOf(entry.OldSoaRecordJson).RefreshSeconds);
        Assert.Equal(7200u, SoaOf(entry.NewSoaRecordJson).RefreshSeconds);
        Assert.Equal(101u, SoaOf(entry.NewSoaRecordJson).Serial);
    }

    private static SoaRecordData SoaOf(string snapshotJson) =>
        Assert.IsType<SoaRecordData>(
            RecordEntityCodec.DecodeTypedData(
                Ninlil.Common.Serializer.JsonSerializer.Deserialize<RecordEntity>(snapshotJson)
            )
        );

    /// <summary>
    ///     This class discards the zone change event: the test reads the journal and runs no
    ///     dispatcher.
    /// </summary>
    private sealed class DiscardingEventPublisher : IDnsServerEventPublisher
    {
        /// <inheritdoc />
        public void Publish(IDnsServerEvent domainEvent) { }
    }
}

/// <summary>This class runs <see cref="ZoneJournalSoaHistoryTestsBase" /> on SQLite (no Docker).</summary>
[Collection(SqliteCollection.Name)]
public sealed class SqliteZoneJournalSoaHistoryTests(SqliteEngineFixture fixture)
    : ZoneJournalSoaHistoryTestsBase(fixture);

/// <summary>This class runs <see cref="ZoneJournalSoaHistoryTestsBase" /> on PostgreSQL (Docker).</summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class PostgreSqlZoneJournalSoaHistoryTests(PostgreSqlEngineFixture fixture)
    : ZoneJournalSoaHistoryTestsBase(fixture);

/// <summary>This class runs <see cref="ZoneJournalSoaHistoryTestsBase" /> on MySQL (Docker).</summary>
[Collection(MySqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MySqlZoneJournalSoaHistoryTests(MySqlEngineFixture fixture)
    : ZoneJournalSoaHistoryTestsBase(fixture);

/// <summary>This class runs <see cref="ZoneJournalSoaHistoryTestsBase" /> on SQL Server (Docker).</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class SqlServerZoneJournalSoaHistoryTests(SqlServerEngineFixture fixture)
    : ZoneJournalSoaHistoryTestsBase(fixture);

/// <summary>
///     This class runs <see cref="ZoneJournalSoaHistoryTestsBase" /> on a MongoDB replica set (Docker).
/// </summary>
[Collection(MongoDbCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbZoneJournalSoaHistoryTests(MongoDbEngineFixture fixture)
    : ZoneJournalSoaHistoryTestsBase(fixture);

/// <summary>
///     This class runs <see cref="ZoneJournalSoaHistoryTestsBase" /> on a standalone MongoDB (Docker).
/// </summary>
[Collection(MongoDbStandaloneCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbStandaloneZoneJournalSoaHistoryTests(
    MongoDbStandaloneEngineFixture fixture
) : ZoneJournalSoaHistoryTestsBase(fixture);
