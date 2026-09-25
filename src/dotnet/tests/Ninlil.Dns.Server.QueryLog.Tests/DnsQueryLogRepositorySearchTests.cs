using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.QueryLog.Tests;

/// <summary>
///     This class tests the search and count queries of
///     <see cref="Ninlil.Dns.Data.EntityFrameworkCore.Repository.DnsQueryLogRepository" />, which
///     run in the database: every filter in <see cref="DnsQueryLogSearchCriteria" />, pagination,
///     and the bulk deletes used by <c>QueryLogController</c> and
///     <see cref="DnsQueryLogCleanupService" />. It uses <see cref="QueryLogTestHost" /> (SQLite).
/// </summary>
public sealed class DnsQueryLogRepositorySearchTests : IAsyncLifetime
{
    private readonly QueryLogTestHost _testHost = new();

    public Task InitializeAsync() => _testHost.InitializeAsync();

    public Task DisposeAsync() => _testHost.DisposeAsync().AsTask();

    /// <summary>
    ///     This method seeds a small, varied set of entries: two client addresses, two queried
    ///     names (one a substring of the other), A and AAAA queries, and a successful and a failed
    ///     response, so each filter can be tested against the same baseline.
    /// </summary>
    private async Task SeedAsync()
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsQueryLogRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsQueryLogRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        DateTimeOffset baseTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        DnsQueryLogEntryEntity[] entries =
        [
            NewEntry(
                baseTime,
                "203.0.113.10",
                "www.example.com",
                DnsResourceRecordTypeEnum.A,
                DnsResponseCodeEnum.NoError
            ),
            NewEntry(
                baseTime.AddMinutes(1),
                "203.0.113.10",
                "mail.example.com",
                DnsResourceRecordTypeEnum.Aaaa,
                DnsResponseCodeEnum.NoError
            ),
            NewEntry(
                baseTime.AddMinutes(2),
                "203.0.113.20",
                "www.example.com",
                DnsResourceRecordTypeEnum.A,
                DnsResponseCodeEnum.NxDomain
            ),
            NewEntry(
                baseTime.AddMinutes(3),
                "203.0.113.20",
                "other.test",
                DnsResourceRecordTypeEnum.A,
                DnsResponseCodeEnum.NoError
            ),
        ];

        foreach (DnsQueryLogEntryEntity entry in entries)
            await repository.AddAsync(entry);

        await unitOfWork.SaveChangesAsync();
    }

    private static DnsQueryLogEntryEntity NewEntry(
        DateTimeOffset occurredAtUtc,
        string clientAddress,
        string queriedName,
        DnsResourceRecordTypeEnum queriedType,
        DnsResponseCodeEnum responseCode
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = occurredAtUtc,
            OccurredAtUtc = occurredAtUtc,
            ClientAddress = clientAddress,
            Protocol = DnsTransportProtocolEnum.Udp,
            QueriedName = queriedName,
            QueriedType = queriedType,
            ResponseCode = responseCode,
            AnswerCount = responseCode == DnsResponseCodeEnum.NoError ? 1 : 0,
        };

    [Fact]
    public async Task SearchAsync_WithNoCriteria_ReturnsEveryEntryNewestFirst()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsQueryLogRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsQueryLogRepository>();

        IReadOnlyList<DnsQueryLogEntryEntity> results = await repository.SearchAsync(
            new DnsQueryLogSearchCriteria(),
            skip: 0,
            take: 50
        );

        Assert.Equal(4, results.Count);
        Assert.Equal("other.test", results[0].QueriedName);
        Assert.Equal("www.example.com", results[3].QueriedName);
    }

    [Fact]
    public async Task SearchAsync_FilteredByClientAddress_ReturnsOnlyThatClientsEntries()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsQueryLogRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsQueryLogRepository>();

        IReadOnlyList<DnsQueryLogEntryEntity> results = await repository.SearchAsync(
            new DnsQueryLogSearchCriteria(ClientAddress: "203.0.113.20"),
            skip: 0,
            take: 50
        );

        Assert.Equal(2, results.Count);
        Assert.All(results, entry => Assert.Equal("203.0.113.20", entry.ClientAddress));
    }

    [Fact]
    public async Task SearchAsync_FilteredByDomainSubstring_MatchesCaseInsensitively()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsQueryLogRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsQueryLogRepository>();

        IReadOnlyList<DnsQueryLogEntryEntity> results = await repository.SearchAsync(
            new DnsQueryLogSearchCriteria(DomainContains: "EXAMPLE"),
            skip: 0,
            take: 50
        );

        Assert.Equal(3, results.Count);
        Assert.DoesNotContain(results, entry => entry.QueriedName == "other.test");
    }

    [Fact]
    public async Task SearchAsync_FilteredByQueriedType_ReturnsOnlyThatType()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsQueryLogRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsQueryLogRepository>();

        IReadOnlyList<DnsQueryLogEntryEntity> results = await repository.SearchAsync(
            new DnsQueryLogSearchCriteria(QueriedType: DnsResourceRecordTypeEnum.Aaaa),
            skip: 0,
            take: 50
        );

        Assert.Single(results);
        Assert.Equal("mail.example.com", results[0].QueriedName);
    }

    [Fact]
    public async Task SearchAsync_FilteredByResponseCode_ReturnsOnlyThatCode()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsQueryLogRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsQueryLogRepository>();

        IReadOnlyList<DnsQueryLogEntryEntity> results = await repository.SearchAsync(
            new DnsQueryLogSearchCriteria(ResponseCode: DnsResponseCodeEnum.NxDomain),
            skip: 0,
            take: 50
        );

        Assert.Single(results);
        Assert.Equal(DnsResponseCodeEnum.NxDomain, results[0].ResponseCode);
    }

    [Fact]
    public async Task SearchAsync_FilteredByDateRange_ExcludesEntriesOutsideIt()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsQueryLogRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsQueryLogRepository>();

        DateTimeOffset baseTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        IReadOnlyList<DnsQueryLogEntryEntity> results = await repository.SearchAsync(
            new DnsQueryLogSearchCriteria(
                FromUtc: baseTime.AddMinutes(1),
                ToUtc: baseTime.AddMinutes(2)
            ),
            skip: 0,
            take: 50
        );

        Assert.Equal(2, results.Count);
        Assert.DoesNotContain(results, entry => entry.QueriedName == "other.test");
        Assert.DoesNotContain(
            results,
            entry => entry.QueriedName == "www.example.com" && entry.ClientAddress == "203.0.113.10"
        );
    }

    /// <summary>
    ///     This test checks that <see cref="DnsQueryLogSearchCriteria.ClientIdentifier" /> and
    ///     <see cref="DnsQueryLogSearchCriteria.ClientProtocolVersion" /> together narrow a search
    ///     to the entries attributed to one client when they were written, the query
    ///     <c>Ninlil.Web.Controllers.ClientActivityController</c> makes.
    /// </summary>
    [Fact]
    public async Task SearchAsync_FilteredByClientIdentifier_ReturnsOnlyThatClientsEntries()
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsQueryLogRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsQueryLogRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        DateTimeOffset baseTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        DnsQueryLogEntryEntity entryForClientA = NewEntry(
            baseTime,
            "203.0.113.10",
            "a.example.com",
            DnsResourceRecordTypeEnum.A,
            DnsResponseCodeEnum.NoError
        );
        entryForClientA.ClientIdentifier = "AA:BB:CC:DD:EE:01";
        entryForClientA.ClientProtocolVersion = InternetProtocolVersionEnum.IPv4;

        DnsQueryLogEntryEntity entryForClientB = NewEntry(
            baseTime.AddMinutes(1),
            "203.0.113.20",
            "b.example.com",
            DnsResourceRecordTypeEnum.A,
            DnsResponseCodeEnum.NoError
        );
        entryForClientB.ClientIdentifier = "AA:BB:CC:DD:EE:02";
        entryForClientB.ClientProtocolVersion = InternetProtocolVersionEnum.IPv4;

        DnsQueryLogEntryEntity entryWithNoResolvedClient = NewEntry(
            baseTime.AddMinutes(2),
            "203.0.113.30",
            "c.example.com",
            DnsResourceRecordTypeEnum.A,
            DnsResponseCodeEnum.NoError
        );

        foreach (
            DnsQueryLogEntryEntity entry in new[]
            {
                entryForClientA,
                entryForClientB,
                entryWithNoResolvedClient,
            }
        )
            await repository.AddAsync(entry);

        await unitOfWork.SaveChangesAsync();

        IReadOnlyList<DnsQueryLogEntryEntity> results = await repository.SearchAsync(
            new DnsQueryLogSearchCriteria(
                ClientIdentifier: "AA:BB:CC:DD:EE:01",
                ClientProtocolVersion: InternetProtocolVersionEnum.IPv4
            ),
            skip: 0,
            take: 50
        );

        DnsQueryLogEntryEntity onlyResult = Assert.Single(results);
        Assert.Equal(entryForClientA.Id, onlyResult.Id);
    }

    [Fact]
    public async Task CountAsync_MatchesTheSameFilterSearchAsyncApplies()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsQueryLogRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsQueryLogRepository>();

        DnsQueryLogSearchCriteria criteria = new(ClientAddress: "203.0.113.20");

        int count = await repository.CountAsync(criteria);
        IReadOnlyList<DnsQueryLogEntryEntity> results = await repository.SearchAsync(
            criteria,
            skip: 0,
            take: 50
        );

        Assert.Equal(results.Count, count);
    }

    [Fact]
    public async Task SearchAsync_Paginated_RespectsSkipAndTake()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsQueryLogRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsQueryLogRepository>();

        IReadOnlyList<DnsQueryLogEntryEntity> firstPage = await repository.SearchAsync(
            new DnsQueryLogSearchCriteria(),
            skip: 0,
            take: 2
        );
        IReadOnlyList<DnsQueryLogEntryEntity> secondPage = await repository.SearchAsync(
            new DnsQueryLogSearchCriteria(),
            skip: 2,
            take: 2
        );

        Assert.Equal(2, firstPage.Count);
        Assert.Equal(2, secondPage.Count);
        Assert.DoesNotContain(secondPage, entry => firstPage.Any(first => first.Id == entry.Id));
    }

    [Fact]
    public async Task DeleteOlderThanAsync_RemovesOnlyEntriesBeforeTheCutoff()
    {
        await SeedAsync();

        DateTimeOffset baseTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsQueryLogRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsQueryLogRepository>();

        int deleted = await repository.DeleteOlderThanAsync(baseTime.AddMinutes(2));
        int remaining = await repository.CountAsync(new DnsQueryLogSearchCriteria());

        Assert.Equal(2, deleted);
        Assert.Equal(2, remaining);
    }

    [Fact]
    public async Task DeleteExcessAsync_KeepsOnlyTheNewestEntries()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsQueryLogRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsQueryLogRepository>();

        int deleted = await repository.DeleteExcessAsync(2);
        IReadOnlyList<DnsQueryLogEntryEntity> remaining = await repository.SearchAsync(
            new DnsQueryLogSearchCriteria(),
            skip: 0,
            take: 50
        );

        Assert.Equal(2, deleted);
        Assert.Equal(2, remaining.Count);
        Assert.Equal("other.test", remaining[0].QueriedName);
    }

    [Fact]
    public async Task DeleteExcessAsync_WithFewerEntriesThanTheCapDeletesNothing()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsQueryLogRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsQueryLogRepository>();

        int deleted = await repository.DeleteExcessAsync(100);

        Assert.Equal(0, deleted);
    }

    [Fact]
    public async Task ClearAsync_RemovesEveryEntry()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsQueryLogRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsQueryLogRepository>();

        int deleted = await repository.ClearAsync();
        int remaining = await repository.CountAsync(new DnsQueryLogSearchCriteria());

        Assert.Equal(4, deleted);
        Assert.Equal(0, remaining);
    }
}
