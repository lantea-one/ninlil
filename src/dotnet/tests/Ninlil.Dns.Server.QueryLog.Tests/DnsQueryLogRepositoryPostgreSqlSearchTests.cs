using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.QueryLog.Tests;

/// <summary>
///     This class proves <see cref="Ninlil.Dns.Data.EntityFrameworkCore.Repository.DnsQueryLogRepository" />'s
///     domain-substring search matches case-insensitively against a real PostgreSQL server, not
///     only against SQLite (which every other test in this project runs against). PostgreSQL's
///     own <c>LIKE</c> operator is case-sensitive by default, unlike SQLite/MySQL/SqlServer's --
///     so <see cref="DnsQueryLogRepositorySearchTests.SearchAsync_FilteredByDomainSubstring_MatchesCaseInsensitively" />
///     passing against SQLite alone would never have caught a regression here. Requires a working
///     Docker daemon (via Testcontainers); there is no fallback for an environment without one.
/// </summary>
public sealed class DnsQueryLogRepositoryPostgreSqlSearchTests : IAsyncLifetime
{
    private readonly QueryLogPostgreSqlTestHost _testHost = new();

    public Task InitializeAsync() => _testHost.InitializeAsync();

    public Task DisposeAsync() => _testHost.DisposeAsync().AsTask();

    [Fact]
    public async Task SearchAsync_FilteredByDomainSubstring_MatchesCaseInsensitivelyOnPostgreSql()
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsQueryLogRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsQueryLogRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // Seed one entry whose queried name is mixed-case, and one unrelated entry that must
        // never match, so a search that accidentally matched everything (or nothing) would still
        // be caught.
        DnsQueryLogEntryEntity matchingEntry = new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            ClientAddress = "203.0.113.10",
            Protocol = DnsTransportProtocolEnum.Udp,
            QueriedName = "www.Example.com",
            QueriedType = DnsResourceRecordTypeEnum.A,
            ResponseCode = DnsResponseCodeEnum.NoError,
            AnswerCount = 1,
        };
        DnsQueryLogEntryEntity nonMatchingEntry = new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            ClientAddress = "203.0.113.10",
            Protocol = DnsTransportProtocolEnum.Udp,
            QueriedName = "other.test",
            QueriedType = DnsResourceRecordTypeEnum.A,
            ResponseCode = DnsResponseCodeEnum.NoError,
            AnswerCount = 1,
        };
        await repository.AddAsync(matchingEntry);
        await repository.AddAsync(nonMatchingEntry);
        await unitOfWork.SaveChangesAsync();

        // The stored name is "www.Example.com" (capital E); the search term below is
        // "EXAMPLE" (all caps) -- neither casing matches the other literally, so this only
        // passes if the repository is genuinely matching case-insensitively against PostgreSQL,
        // proving the EF.Functions.ILike branch is actually wired up and translating correctly
        // rather than silently falling back to PostgreSQL's own case-sensitive EF.Functions.Like.
        IReadOnlyList<DnsQueryLogEntryEntity> results = await repository.SearchAsync(
            new DnsQueryLogSearchCriteria(DomainContains: "EXAMPLE"),
            skip: 0,
            take: 50
        );

        Assert.Single(results);
        Assert.Equal("www.Example.com", results[0].QueriedName);
    }
}
