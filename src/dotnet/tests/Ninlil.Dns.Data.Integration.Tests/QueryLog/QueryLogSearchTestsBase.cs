using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.QueryLog;

/// <summary>
///     This class holds the query-log domain search tests that run against every engine's real
///     server: one subclass per engine supplies the fixture. The search term is matched
///     literally, so <c>_</c>, <c>%</c> and <c>[</c> in a term are ordinary characters, and the
///     match ignores case. Each test seeds a fresh per-test database. The SQLite counterpart is
///     <c>DnsQueryLogRepositorySearchTests</c> in <c>Ninlil.Dns.Server.QueryLog.Tests</c>.
/// </summary>
public abstract class QueryLogSearchTestsBase(EngineFixtureBase fixture) : IAsyncLifetime
{
    private EngineDatabase _database;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _database = await fixture.CreateDatabaseAsync();

        // One name per special character, plus two decoys: "xdmarc..." is what an unescaped
        // underscore would also match, and "b.example" is what an unescaped "[b]" would match
        // on SQL Server (a character class).
        await using AsyncServiceScope scope = _database.Services.CreateAsyncScope();
        IDnsQueryLogRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsQueryLogRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        DateTimeOffset baseTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        string[] names =
        [
            "_dmarc.example.com",
            "xdmarc.example.com",
            "100%.example",
            "a[b].example",
            "b.example",
        ];

        for (int index = 0; index < names.Length; index++)
            await repository.AddAsync(
                new DnsQueryLogEntryEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = baseTime.AddMinutes(index),
                    OccurredAtUtc = baseTime.AddMinutes(index),
                    ClientAddress = "203.0.113.10",
                    Protocol = DnsTransportProtocolEnum.Udp,
                    QueriedName = names[index],
                    QueriedType = DnsResourceRecordTypeEnum.A,
                    ResponseCode = DnsResponseCodeEnum.NoError,
                    AnswerCount = 1,
                }
            );

        await unitOfWork.SaveChangesAsync();
    }

    /// <inheritdoc />
    public Task DisposeAsync() => _database.DisposeAsync().AsTask();

    private async Task<(IReadOnlyList<string> Names, int Count)> SearchAsync(string term)
    {
        await using AsyncServiceScope scope = _database.Services.CreateAsyncScope();
        IDnsQueryLogRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsQueryLogRepository>();
        DnsQueryLogSearchCriteria criteria = new(DomainContains: term);

        IReadOnlyList<DnsQueryLogEntryEntity> results = await repository.SearchAsync(
            criteria,
            skip: 0,
            take: 50
        );

        return (
            results.Select(entry => entry.QueriedName).ToList(),
            await repository.CountAsync(criteria)
        );
    }

    [Fact]
    public async Task SearchAsync_TermWithUnderscore_MatchesTheUnderscoreLiterally()
    {
        (IReadOnlyList<string> names, int count) = await SearchAsync("_dmarc");

        Assert.Equal(["_dmarc.example.com"], names);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task SearchAsync_TermWithPercent_MatchesThePercentLiterally()
    {
        (IReadOnlyList<string> names, int count) = await SearchAsync("%");

        Assert.Equal(["100%.example"], names);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task SearchAsync_TermWithBrackets_MatchesTheBracketsLiterally()
    {
        (IReadOnlyList<string> names, int count) = await SearchAsync("[b]");

        Assert.Equal(["a[b].example"], names);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task SearchAsync_TermInOtherCase_StillMatches()
    {
        (IReadOnlyList<string> names, int count) = await SearchAsync("_DMARC");

        Assert.Equal(["_dmarc.example.com"], names);
        Assert.Equal(1, count);
    }
}
