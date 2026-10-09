using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.QueryLog.Tests;

/// <summary>
///     This class tests that the domain-substring search in
///     <see cref="Ninlil.Dns.Data.EntityFrameworkCore.Repository.DnsQueryLogRepository" /> is
///     case-insensitive on PostgreSQL, using <see cref="QueryLogPostgreSqlTestHost" />. The other
///     tests in this project use SQLite, whose <c>LIKE</c> ignores ASCII case, so
///     <see cref="DnsQueryLogRepositorySearchTests.SearchAsync_FilteredByDomainSubstring_MatchesCaseInsensitively" />
///     alone would not catch a PostgreSQL regression. It needs a working Docker daemon, so it
///     carries the <c>RequiresDocker</c> category: the pull-request pipeline job has no Docker
///     socket and runs with <c>--filter "Category!=RequiresDocker"</c>.
/// </summary>
[Trait("Category", "RequiresDocker")]
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

        // One mixed-case entry that should match and one that should not, so a search that
        // matches everything (or nothing) fails.
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

        // The stored name is "www.Example.com" and the search term "EXAMPLE", so this passes
        // only if the PostgreSQL branch uses EF.Functions.ILike rather than the case-sensitive
        // EF.Functions.Like.
        IReadOnlyList<DnsQueryLogEntryEntity> results = await repository.SearchAsync(
            new DnsQueryLogSearchCriteria(DomainContains: "EXAMPLE"),
            skip: 0,
            take: 50
        );

        Assert.Single(results);
        Assert.Equal("www.Example.com", results[0].QueriedName);
    }
}
