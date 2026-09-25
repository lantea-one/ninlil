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
///     This class checks that <see cref="ZoneWriteSerializer" /> keeps concurrent
///     <see cref="IZoneUpdateService" /> calls for the same zone (a double submit, a batch import)
///     from interleaving, using the EF Core repositories on a temporary SQLite database. Without
///     it, two calls can read the same state: a new zone could be bootstrapped twice, or two
///     additions could both write "serial plus one" and advance the serial once for two changes.
///     <para>
///         Each test repeats its race many times, because a race can pass by luck once. The tests
///         fail without per-zone serialization.
///     </para>
/// </summary>
public sealed class ZoneUpdateServiceConcurrencyTests : IAsyncLifetime
{
    /// <summary>
    ///     This field contains the path to this test's throwaway SQLite database file,
    ///     created fresh per test class instance and deleted again in <see cref="DisposeAsync" />.
    /// </summary>
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-zone-update-concurrency-tests-{Guid.NewGuid()}.sqlite3"
    );

    /// <summary>
    ///     This field contains the host every test below resolves its own, independent DI scopes
    ///     from shared across every concurrent "request" a test simulates, since
    ///     <see cref="IZoneWriteSerializer" /> is only exercised when every caller
    ///     resolves the same singleton instance of it, the same way every real request in a
    ///     running server would.
    /// </summary>
    private IHost _host;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);
        builder.Services.AddSingleton<IDnsServerEventPublisher, NoopEventPublisher>();
        builder.Services.AddSingleton<IZoneWriteSerializer, ZoneWriteSerializer>();
        builder.Services.AddScoped<IZoneUpdateService, ZoneUpdateService>();

        _host = builder.Build();

        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        Data.EntityFrameworkCore.NinlilDnsContext context =
            scope.ServiceProvider.GetRequiredService<Data.EntityFrameworkCore.NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        _host.Dispose();

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);

        return Task.CompletedTask;
    }

    /// <summary>
    ///     This test simulates two <see cref="IZoneUpdateService.BootstrapZoneAsync" /> calls for
    ///     the same brand-new zone fired concurrently (a double form submit, or a client
    ///     retry that did not fail the first time). Before per-zone serialization
    ///     existed, both calls could observe zero existing records before either committed, race
    ///     each other's "does this zone already have records" check and its insert, and
    ///     -- depending on how far each got before the other's write landed -- leave the
    ///     zone with two SOA records instead of the single one RFC 1035 requires. This runs the
    ///     scenario across many independent zones in a loop, since the exact interleaving needed
    ///     to trigger the bug is timing-dependent and does not reproduce on every single attempt.
    /// </summary>
    [Fact]
    public async Task ConcurrentBootstrapAttemptsForTheSameZone_NeverProduceMoreThanOneSoaRecord()
    {
        for (int iteration = 0; iteration < 100; iteration++)
        {
            Guid zoneId = Guid.NewGuid();
            string zoneName = $"race{iteration}.example.com";

            await using (AsyncServiceScope setupScope = _host.Services.CreateAsyncScope())
            {
                IZoneRepository zoneRepository =
                    setupScope.ServiceProvider.GetRequiredService<IZoneRepository>();
                IUnitOfWork unitOfWork =
                    setupScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                await zoneRepository.AddAsync(
                    new ZoneEntity
                    {
                        Id = zoneId,
                        Name = zoneName,
                        Type = ZoneTypeEnum.Primary,
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                    }
                );
                await unitOfWork.SaveChangesAsync();
            }

            Task first = BootstrapZoneWithSoaAsync(zoneId, zoneName);
            Task second = BootstrapZoneWithSoaAsync(zoneId, zoneName);
            Task[] tasks = [first, second];

            // Exactly one of the two must succeed and the other must fail cleanly with
            // BootstrapZoneAsync's own, pre-existing "already has records" message -- never both
            // succeeding (which would mean two SOA records got inserted) and never both failing.
            await Task.WhenAll(
                tasks.Select(async task =>
                {
                    try
                    {
                        await task;
                    }
                    catch (InvalidOperationException)
                    {
                        // Expected for one of the two -- verified below via the actual
                        // record count, not just the fact that something threw.
                    }
                })
            );

            int succeededCount = tasks.Count(task => task.IsCompletedSuccessfully);
            Assert.True(
                succeededCount == 1,
                $"Iteration {iteration}: expected exactly 1 of 2 concurrent bootstrap attempts to succeed, got {succeededCount}."
            );

            await using AsyncServiceScope verifyScope = _host.Services.CreateAsyncScope();
            IRecordRepository recordRepository =
                verifyScope.ServiceProvider.GetRequiredService<IRecordRepository>();
            IReadOnlyList<RecordEntity> soaRecords = await recordRepository.FindByNameAsync(
                zoneId,
                zoneName,
                DnsResourceRecordTypeEnum.Soa
            );
            Assert.True(
                soaRecords.Count == 1,
                $"Iteration {iteration}: expected exactly 1 SOA record for the zone, found {soaRecords.Count}."
            );
        }
    }

    /// <summary>
    ///     This test simulates a zone's SOA record already having committed, then two ordinary
    ///     "add a record" requests for that same zone fired concurrently -- e.g. a batch import,
    ///     or two record-creation form submits close together -- neither of which is the bootstrap
    ///     path; both go straight to <see cref="IZoneUpdateService.ApplyChangesAsync" />. Before
    ///     per-zone serialization existed, both calls could read the same "current" SOA serial
    ///     before either committed its increment, and the second write to land would silently
    ///     overwrite the first's, leaving the serial advanced by only one increment despite two
    ///     real content changes having happened -- a lost update, and a real RFC 1035 correctness
    ///     violation, even though both records themselves were still correctly inserted.
    /// </summary>
    [Fact]
    public async Task ConcurrentApplyChangesForTheSameZone_AdvancesTheSerialOnceForEachRealChange()
    {
        Guid zoneId = Guid.NewGuid();
        const string zoneName = "concurrent-apply.example.com";

        await using (AsyncServiceScope setupScope = _host.Services.CreateAsyncScope())
        {
            IZoneRepository zoneRepository =
                setupScope.ServiceProvider.GetRequiredService<IZoneRepository>();
            IZoneUpdateService zoneUpdateService =
                setupScope.ServiceProvider.GetRequiredService<IZoneUpdateService>();
            IUnitOfWork unitOfWork = setupScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await zoneRepository.AddAsync(
                new ZoneEntity
                {
                    Id = zoneId,
                    Name = zoneName,
                    Type = ZoneTypeEnum.Primary,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                }
            );
            await unitOfWork.SaveChangesAsync();

            await zoneUpdateService.BootstrapZoneAsync(zoneId, [BuildSoaRecord(zoneId, zoneName)]);
        }

        Task addFirstRecord = ApplyOneRecordChangeAsync(zoneId, zoneName, "a1", "192.0.2.1");
        Task addSecondRecord = ApplyOneRecordChangeAsync(zoneId, zoneName, "a2", "192.0.2.2");

        await Task.WhenAll(addFirstRecord, addSecondRecord);

        await using AsyncServiceScope verifyScope = _host.Services.CreateAsyncScope();
        IRecordRepository recordRepository =
            verifyScope.ServiceProvider.GetRequiredService<IRecordRepository>();
        IReadOnlyList<RecordEntity> allRecords = await recordRepository.ListByZoneIdAsync(zoneId);
        IReadOnlyList<RecordEntity> soaRecords = await recordRepository.FindByNameAsync(
            zoneId,
            zoneName,
            DnsResourceRecordTypeEnum.Soa
        );
        SoaRecordData soaData = Assert.IsType<SoaRecordData>(
            RecordEntityCodec.DecodeTypedData(soaRecords[0])
        );

        // Both A records must exist (the two ApplyChangesAsync calls must not have clobbered each
        // other's insert), and the serial must have advanced by 2 from its bootstrap
        // value of 1 -- one bump per real content change -- not by 1, which is what a lost update
        // from two concurrent read-then-write SOA increments would leave behind.
        Assert.Equal(3, allRecords.Count); // SOA + 2 A records.
        Assert.Equal(3u, soaData.Serial);
    }

    /// <summary>
    ///     This method bootstraps <paramref name="zoneId" /> with a fresh SOA record, resolving
    ///     its independent DI scope first -- mirroring how a real HTTP request resolves an
    ///     independent scope per call, which is the condition needed for
    ///     <see cref="IZoneWriteSerializer" /> to be meaningfully exercised (a shared scope would
    ///     share a single <see cref="Data.EntityFrameworkCore.NinlilDnsContext" /> and mask the
    ///     underlying race entirely).
    /// </summary>
    private Task BootstrapZoneWithSoaAsync(Guid zoneId, string zoneName) =>
        Task.Run(async () =>
        {
            await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
            IZoneUpdateService zoneUpdateService =
                scope.ServiceProvider.GetRequiredService<IZoneUpdateService>();
            await zoneUpdateService.BootstrapZoneAsync(zoneId, [BuildSoaRecord(zoneId, zoneName)]);
        });

    /// <summary>
    ///     This method applies a single A-record addition to <paramref name="zoneId" /> through
    ///     <see cref="IZoneUpdateService.ApplyChangesAsync" />, resolving its independent DI
    ///     scope first for the same reason <see cref="BootstrapZoneWithSoaAsync" /> does.
    /// </summary>
    private Task ApplyOneRecordChangeAsync(
        Guid zoneId,
        string zoneName,
        string label,
        string ipAddress
    ) =>
        Task.Run(async () =>
        {
            await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
            IZoneUpdateService zoneUpdateService =
                scope.ServiceProvider.GetRequiredService<IZoneUpdateService>();
            RecordEntity record = new()
            {
                Id = Guid.NewGuid(),
                ZoneId = zoneId,
                Name = $"{label}.{zoneName}",
                Type = DnsResourceRecordTypeEnum.A,
                Class = DnsClassEnum.Internet,
                TimeToLiveSeconds = 300,
                DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                    new ARecordData(IPAddress.Parse(ipAddress))
                ),
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
            await zoneUpdateService.ApplyChangesAsync(zoneId, [record], []);
        });

    /// <summary>
    ///     This method builds a not-yet-persisted SOA record entity for <paramref name="zoneId" />,
    ///     shared by every test above that needs to bootstrap a zone.
    /// </summary>
    private static RecordEntity BuildSoaRecord(Guid zoneId, string zoneName) =>
        new()
        {
            Id = Guid.NewGuid(),
            ZoneId = zoneId,
            Name = zoneName,
            Type = DnsResourceRecordTypeEnum.Soa,
            Class = DnsClassEnum.Internet,
            TimeToLiveSeconds = 3600,
            DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                new SoaRecordData(
                    Common.Wire.DnsName.Parse($"ns1.{zoneName}"),
                    Common.Wire.DnsName.Parse($"admin.{zoneName}"),
                    1,
                    3600,
                    600,
                    604800,
                    300
                )
            ),
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

    /// <summary>
    ///     This class stands in for the real channel-backed <c>DnsServerEventPublisher</c> so
    ///     these tests do not need the full background event dispatcher running -- neither test
    ///     above asserts anything about published events, so every publish is simply discarded.
    /// </summary>
    private sealed class NoopEventPublisher : IDnsServerEventPublisher
    {
        /// <inheritdoc />
        public void Publish(IDnsServerEvent domainEvent) { }
    }
}
