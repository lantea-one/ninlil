using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Repository;

/// <summary>
///     This class tests the paging of <see cref="Ninlil.Dns.Data.EntityFrameworkCore.Repository.RecordRepository" /> (<c>SearchByZoneIdAsync</c>, <c>CountByZoneIdAsync</c>) against a
///     temporary SQLite database (<see cref="RepositorySearchTestHost" />): the page is cut by the
///     database with <c>ORDER BY</c>/<c>OFFSET</c>/<c>LIMIT</c>, as <c>RecordsController</c>'s listing needs, rather
///     than in memory. It also
///     checks that another zone's records never appear.
/// </summary>
public sealed class RecordRepositorySearchTests : IAsyncLifetime
{
    private readonly RepositorySearchTestHost _testHost = new();
    private Guid _zoneId;
    private Guid _otherZoneId;

    public Task InitializeAsync() => _testHost.InitializeAsync();

    public Task DisposeAsync() => _testHost.DisposeAsync().AsTask();

    /// <summary>
    ///     This method seeds one zone with five A records at out-of-order owner
    ///     names, plus a second, unrelated zone with its record -- so both the real
    ///     <c>ORDER BY</c> translation and the zone-scoping <c>WHERE</c> clause are exercised
    ///     against the same database.
    /// </summary>
    private async Task SeedAsync()
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IZoneRepository zoneRepository =
            scope.ServiceProvider.GetRequiredService<IZoneRepository>();
        IRecordRepository recordRepository =
            scope.ServiceProvider.GetRequiredService<IRecordRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        _zoneId = Guid.NewGuid();
        _otherZoneId = Guid.NewGuid();

        await zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = _zoneId,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Name = "records.example.com",
                Type = ZoneTypeEnum.Primary,
            }
        );
        await zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = _otherZoneId,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Name = "other.example.com",
                Type = ZoneTypeEnum.Primary,
            }
        );

        string[] namesInSeedOrder =
        [
            "delta.records.example.com",
            "bravo.records.example.com",
            "echo.records.example.com",
            "alpha.records.example.com",
            "charlie.records.example.com",
        ];

        foreach (string name in namesInSeedOrder)
            await recordRepository.AddAsync(NewARecord(_zoneId, name));

        // A record in a different zone -- must never appear in a search scoped to _zoneId.
        await recordRepository.AddAsync(NewARecord(_otherZoneId, "www.other.example.com"));

        await unitOfWork.SaveChangesAsync();
    }

    private static RecordEntity NewARecord(Guid zoneId, string name) =>
        new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ZoneId = zoneId,
            Name = name,
            Type = DnsResourceRecordTypeEnum.A,
            TimeToLiveSeconds = 300,
            DataJson = """{"Address":"192.0.2.1"}""",
        };

    [Fact]
    public async Task SearchByZoneIdAsync_ReturnsOnlyThatZonesRecordsOrderedByName()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IRecordRepository repository =
            scope.ServiceProvider.GetRequiredService<IRecordRepository>();

        IReadOnlyList<RecordEntity> results = await repository.SearchByZoneIdAsync(
            _zoneId,
            skip: 0,
            take: 50
        );

        Assert.Equal(5, results.Count);
        Assert.All(results, record => Assert.Equal(_zoneId, record.ZoneId));
        Assert.Equal(
            [
                "alpha.records.example.com",
                "bravo.records.example.com",
                "charlie.records.example.com",
                "delta.records.example.com",
                "echo.records.example.com",
            ],
            [.. results.Select(record => record.Name)]
        );
    }

    [Fact]
    public async Task SearchByZoneIdAsync_Paginated_ReturnsTheCorrectSliceForEachPage()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IRecordRepository repository =
            scope.ServiceProvider.GetRequiredService<IRecordRepository>();

        IReadOnlyList<RecordEntity> firstPage = await repository.SearchByZoneIdAsync(
            _zoneId,
            skip: 0,
            take: 2
        );
        IReadOnlyList<RecordEntity> secondPage = await repository.SearchByZoneIdAsync(
            _zoneId,
            skip: 2,
            take: 2
        );

        Assert.Equal(
            ["alpha.records.example.com", "bravo.records.example.com"],
            [.. firstPage.Select(record => record.Name)]
        );
        Assert.Equal(
            ["charlie.records.example.com", "delta.records.example.com"],
            [.. secondPage.Select(record => record.Name)]
        );
        Assert.DoesNotContain(secondPage, record => firstPage.Any(first => first.Id == record.Id));
    }

    [Fact]
    public async Task CountByZoneIdAsync_CountsOnlyThatZonesRecords()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IRecordRepository repository =
            scope.ServiceProvider.GetRequiredService<IRecordRepository>();

        int zoneCount = await repository.CountByZoneIdAsync(_zoneId);
        int otherZoneCount = await repository.CountByZoneIdAsync(_otherZoneId);

        Assert.Equal(5, zoneCount);
        Assert.Equal(1, otherZoneCount);
    }
}
