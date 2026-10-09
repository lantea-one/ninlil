using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.QueryLog.Tests;

/// <summary>
///     This class tests <c>DeleteExcessAsync</c> (retention by count) of
///     <see cref="Ninlil.Dns.Data.EntityFrameworkCore.Repository.DnsQueryLogRepository" /> on
///     SQLite through <see cref="QueryLogTestHost" />. The same rules run on every other engine
///     in <c>Ninlil.Dns.Data.Integration.Tests</c>.
/// </summary>
public sealed class DnsQueryLogRepositoryRetentionTests : IAsyncLifetime
{
    private static readonly DateTimeOffset BaseTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly QueryLogTestHost _testHost = new();

    public Task InitializeAsync() => _testHost.InitializeAsync();

    public Task DisposeAsync() => _testHost.DisposeAsync().AsTask();

    private static DnsQueryLogEntryEntity NewEntry(int number, DateTimeOffset occurredAtUtc) =>
        new()
        {
            Id = Guid.Parse($"{number:x8}-0000-0000-0000-{number:x12}"),
            CreatedAtUtc = occurredAtUtc,
            OccurredAtUtc = occurredAtUtc,
            ClientAddress = "203.0.113.10",
            Protocol = DnsTransportProtocolEnum.Udp,
            QueriedName = $"host{number}.example",
            QueriedType = DnsResourceRecordTypeEnum.A,
            ResponseCode = DnsResponseCodeEnum.NoError,
            AnswerCount = 1,
        };

    private async Task SeedAsync(IEnumerable<DnsQueryLogEntryEntity> entries)
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<IDnsQueryLogRepository>()
            .AddRangeAsync([.. entries]);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    private async Task<int> DeleteExcessAsync(int max)
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<IDnsQueryLogRepository>()
            .DeleteExcessAsync(max);
    }

    private async Task<List<int>> RemainingNumbersAsync()
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IReadOnlyList<DnsQueryLogEntryEntity> all = await scope
            .ServiceProvider.GetRequiredService<IDnsQueryLogRepository>()
            .SearchAsync(new DnsQueryLogSearchCriteria(), 0, int.MaxValue);
        return [.. all.Select(entry => Convert.ToInt32(entry.QueriedName[4..^8])).OrderBy(n => n)];
    }

    /// <summary>A thousand rows, keep 600: the 600 newest remain.</summary>
    [Fact]
    public async Task DeleteExcessAsync_ThousandRowsKeepSixHundred_KeepsTheNewestSixHundred()
    {
        await SeedAsync(Enumerable.Range(0, 1000).Select(n => NewEntry(n, BaseTime.AddSeconds(n))));

        Assert.Equal(400, await DeleteExcessAsync(600));
        Assert.Equal(Enumerable.Range(400, 600).ToList(), await RemainingNumbersAsync());
    }

    /// <summary>
    ///     Ten rows share the cutoff timestamp and the limit splits them: exactly the limit
    ///     remains and the kept tied rows have the greatest identifiers.
    /// </summary>
    [Fact]
    public async Task DeleteExcessAsync_TenRowsShareTheCutoff_KeepsTheGreatestIdsAmongThem()
    {
        List<DnsQueryLogEntryEntity> entries = [];
        for (int n = 0; n < 10; n++)
            entries.Add(NewEntry(n, BaseTime.AddMinutes(n)));
        for (int n = 10; n < 20; n++)
            entries.Add(NewEntry(n, BaseTime.AddHours(1)));
        for (int n = 20; n < 30; n++)
            entries.Add(NewEntry(n, BaseTime.AddHours(2).AddMinutes(n)));
        await SeedAsync(entries);

        Assert.Equal(16, await DeleteExcessAsync(14));
        Assert.Equal(Enumerable.Range(16, 14).ToList(), await RemainingNumbersAsync());
    }

    /// <summary>Fewer rows than the limit, or exactly the limit: nothing is deleted.</summary>
    [Theory]
    [InlineData(100)]
    [InlineData(50)]
    public async Task DeleteExcessAsync_RowCountAtOrBelowTheLimit_DeletesNothing(int max)
    {
        await SeedAsync(Enumerable.Range(0, 50).Select(n => NewEntry(n, BaseTime.AddSeconds(n))));

        Assert.Equal(0, await DeleteExcessAsync(max));
        Assert.Equal(50, (await RemainingNumbersAsync()).Count);
    }
}
