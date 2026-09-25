using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.QueryLog.Tests;

/// <summary>
///     This class tests the retention sweep of <see cref="DnsQueryLogCleanupService" />: the age
///     bound, the count bound, both off, and running while logging is disabled. It calls
///     <see cref="DnsQueryLogCleanupService.SweepAsync" /> directly, rather than waiting for
///     <see cref="QueryLoggingOptions.CleanupInterval" />, against <see cref="QueryLogTestHost" />
///     (SQLite).
/// </summary>
public sealed class DnsQueryLogCleanupServiceTests : IAsyncLifetime
{
    private readonly QueryLogTestHost _testHost = new();

    public Task InitializeAsync() => _testHost.InitializeAsync();

    public Task DisposeAsync() => _testHost.DisposeAsync().AsTask();

    private async Task SeedAsync(int count, DateTimeOffset startingAtUtc)
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IDnsQueryLogRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsQueryLogRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        for (int index = 0; index < count; index++)
        {
            DateTimeOffset occurredAtUtc = startingAtUtc.AddMinutes(index);
            await repository.AddAsync(
                new DnsQueryLogEntryEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = occurredAtUtc,
                    OccurredAtUtc = occurredAtUtc,
                    ClientAddress = "203.0.113.5",
                    Protocol = DnsTransportProtocolEnum.Udp,
                    QueriedName = $"entry{index}.example.test",
                    QueriedType = DnsResourceRecordTypeEnum.A,
                    ResponseCode = DnsResponseCodeEnum.NoError,
                    AnswerCount = 1,
                }
            );
        }

        await unitOfWork.SaveChangesAsync();
    }

    private DnsQueryLogCleanupService CreateService(QueryLoggingOptions options) =>
        new(
            _testHost.Host.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            NullLogger<DnsQueryLogCleanupService>.Instance
        );

    [Fact]
    public async Task SweepAsync_RemovesEntriesOlderThanTheRetentionPeriod()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Five entries from 5 to 45 days old; a 30-day retention period removes the two older
        // than that.
        await using (AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope())
        {
            IDnsQueryLogRepository repository =
                scope.ServiceProvider.GetRequiredService<IDnsQueryLogRepository>();
            IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            foreach (int daysAgo in new[] { 45, 35, 25, 15, 5 })
                await repository.AddAsync(
                    new DnsQueryLogEntryEntity
                    {
                        Id = Guid.NewGuid(),
                        CreatedAtUtc = now.AddDays(-daysAgo),
                        OccurredAtUtc = now.AddDays(-daysAgo),
                        ClientAddress = "203.0.113.5",
                        Protocol = DnsTransportProtocolEnum.Udp,
                        QueriedName = $"day-{daysAgo}.example.test",
                        QueriedType = DnsResourceRecordTypeEnum.A,
                        ResponseCode = DnsResponseCodeEnum.NoError,
                        AnswerCount = 1,
                    }
                );

            await unitOfWork.SaveChangesAsync();
        }

        DnsQueryLogCleanupService service = CreateService(
            new QueryLoggingOptions
            {
                RetentionPeriod = TimeSpan.FromDays(30),
                MaxRetainedEntries = 0,
            }
        );

        (int deletedByAge, int deletedByCount) = await service.SweepAsync();

        await using AsyncServiceScope verifyScope = _testHost.Host.Services.CreateAsyncScope();
        int remaining = await verifyScope
            .ServiceProvider.GetRequiredService<IDnsQueryLogRepository>()
            .CountAsync(new DnsQueryLogSearchCriteria());

        Assert.Equal(2, deletedByAge);
        Assert.Equal(0, deletedByCount);
        Assert.Equal(3, remaining);
    }

    [Fact]
    public async Task SweepAsync_RemovesEntriesBeyondTheMaximumRetainedCount()
    {
        await SeedAsync(5, DateTimeOffset.UtcNow.AddMinutes(-30));

        DnsQueryLogCleanupService service = CreateService(
            new QueryLoggingOptions { RetentionPeriod = TimeSpan.Zero, MaxRetainedEntries = 2 }
        );

        (int deletedByAge, int deletedByCount) = await service.SweepAsync();

        await using AsyncServiceScope verifyScope = _testHost.Host.Services.CreateAsyncScope();
        int remaining = await verifyScope
            .ServiceProvider.GetRequiredService<IDnsQueryLogRepository>()
            .CountAsync(new DnsQueryLogSearchCriteria());

        Assert.Equal(0, deletedByAge);
        Assert.Equal(3, deletedByCount);
        Assert.Equal(2, remaining);
    }

    [Fact]
    public async Task SweepAsync_WithBothBoundsDisabled_DeletesNothing()
    {
        await SeedAsync(3, DateTimeOffset.UtcNow.AddDays(-100));

        DnsQueryLogCleanupService service = CreateService(
            new QueryLoggingOptions { RetentionPeriod = TimeSpan.Zero, MaxRetainedEntries = 0 }
        );

        (int deletedByAge, int deletedByCount) = await service.SweepAsync();

        await using AsyncServiceScope verifyScope = _testHost.Host.Services.CreateAsyncScope();
        int remaining = await verifyScope
            .ServiceProvider.GetRequiredService<IDnsQueryLogRepository>()
            .CountAsync(new DnsQueryLogSearchCriteria());

        Assert.Equal(0, deletedByAge);
        Assert.Equal(0, deletedByCount);
        Assert.Equal(3, remaining);
    }

    [Fact]
    public async Task SweepAsync_RunsRegardlessOfWhetherLoggingItselfIsEnabled()
    {
        // QueryLoggingOptions.Enable only controls whether DnsQueryLogSubscriber writes new
        // entries. SweepAsync does not check it, so entries from before logging was disabled
        // still age out.
        await SeedAsync(3, DateTimeOffset.UtcNow.AddDays(-100));

        DnsQueryLogCleanupService service = CreateService(
            new QueryLoggingOptions
            {
                Enable = false,
                RetentionPeriod = TimeSpan.FromDays(30),
                MaxRetainedEntries = 0,
            }
        );

        (int deletedByAge, int _) = await service.SweepAsync();

        Assert.Equal(3, deletedByAge);
    }
}
