using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.QueryLog.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.QueryLog.Tests;

/// <summary>
///     This class tests <see cref="DnsQueryLogWriterService" /> and <see cref="DnsQueryLogQueue" />:
///     that queued entries are saved in batches of at most 500, that a full queue drops and counts
///     new entries without ever blocking the subscriber, and that stopping the service saves what
///     is still queued. The writer runs against <see cref="QueryLogTestHost" /> (SQLite) through
///     a scope factory that counts <c>SaveChangesAsync</c> calls.
/// </summary>
public sealed class DnsQueryLogWriterServiceTests : IAsyncLifetime
{
    private readonly QueryLogTestHost _testHost = new();

    public Task InitializeAsync() => _testHost.InitializeAsync();

    public Task DisposeAsync() => _testHost.DisposeAsync().AsTask();

    private static DnsQueryLogEntryEntity NewEntry() =>
        new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            ClientAddress = "198.51.100.7",
            Protocol = DnsTransportProtocolEnum.Udp,
            QueriedName = "host.example.test",
            QueriedType = DnsResourceRecordTypeEnum.A,
            ResponseCode = DnsResponseCodeEnum.NoError,
            AnswerCount = 1,
        };

    private static DnsQueryAnsweredEvent NewAnsweredEvent() =>
        new(
            OccurredAtUtc: DateTimeOffset.UtcNow,
            Protocol: DnsTransportProtocolEnum.Udp,
            RemoteAddress: IPAddress.Parse("198.51.100.7"),
            QueriedName: "host.example.test",
            QueriedType: DnsResourceRecordTypeEnum.A,
            ResponseCode: DnsResponseCodeEnum.NoError,
            AnswerCount: 1
        );

    private DnsQueryLogWriterService NewWriter(
        DnsQueryLogQueue queue,
        CountingScopeFactory scopeFactory
    ) =>
        new(queue, scopeFactory, NullLogger<DnsQueryLogWriterService>.Instance)
        {
            FlushInterval = TimeSpan.FromMilliseconds(50),
        };

    private async Task<int> CountRowsAsync()
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IReadOnlyList<DnsQueryLogEntryEntity> entries = await scope
            .ServiceProvider.GetRequiredService<IDnsQueryLogRepository>()
            .ListAsync();
        return entries.Count;
    }

    private async Task WaitForRowsAsync(int expected)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < TimeSpan.FromSeconds(30) && await CountRowsAsync() < expected)
            await Task.Delay(20);
    }

    [Fact]
    public async Task ExecuteAsync_With1200QueuedEntries_SavesThemInThreeBatches()
    {
        DnsQueryLogQueue queue = new();
        CountingScopeFactory scopeFactory = new(
            _testHost.Host.Services.GetRequiredService<IServiceScopeFactory>()
        );

        // Queue everything before the writer starts so the batch boundaries are deterministic.
        for (int i = 0; i < 1200; i++)
            Assert.True(queue.TryEnqueue(NewEntry()));

        DnsQueryLogWriterService writer = NewWriter(queue, scopeFactory);
        await writer.StartAsync(CancellationToken.None);
        await WaitForRowsAsync(1200);
        await writer.StopAsync(CancellationToken.None);

        Assert.Equal(1200, await CountRowsAsync());
        Assert.Equal(3, scopeFactory.SaveChangesCount);
        Assert.Equal(0, queue.DroppedCount);
    }

    [Fact]
    public async Task TryEnqueue_WhenTheQueueIsFull_DropsAndCountsWithoutBlocking()
    {
        DnsQueryLogQueue queue = new(capacity: 5);

        for (int i = 0; i < 5; i++)
            Assert.True(queue.TryEnqueue(NewEntry()));

        // No writer is running, so the queue stays full. Handing 100 entries to the subscriber
        // must complete at once rather than wait for room.
        DnsQueryLogSubscriber subscriber = new(
            queue,
            new StaticOptionsMonitor<QueryLoggingOptions>(
                new QueryLoggingOptions { Enable = true }
            ),
            NullLogger<DnsQueryLogSubscriber>.Instance
        );

        Task handling = Task.Run(async () =>
        {
            for (int i = 0; i < 100; i++)
                await subscriber.HandleAsync(NewAnsweredEvent(), CancellationToken.None);
        });

        Task finished = await Task.WhenAny(handling, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(handling, finished);
        await handling;

        Assert.Equal(100, queue.DroppedCount);
        Assert.False(queue.TryEnqueue(NewEntry()));
        Assert.Equal(101, queue.DroppedCount);
    }

    [Fact]
    public async Task StopAsync_SavesTheEntriesStillQueued()
    {
        DnsQueryLogQueue queue = new();
        CountingScopeFactory scopeFactory = new(
            _testHost.Host.Services.GetRequiredService<IServiceScopeFactory>()
        );

        DnsQueryLogWriterService writer = NewWriter(queue, scopeFactory);

        for (int i = 0; i < 700; i++)
            queue.TryEnqueue(NewEntry());

        // Stopping right after the start leaves most of the queue to the drain on stop. How the
        // 700 entries split between the loop and the drain is timing-dependent, so only the row
        // count is exact.
        await writer.StartAsync(CancellationToken.None);
        await writer.StopAsync(CancellationToken.None);

        Assert.Equal(700, await CountRowsAsync());
        Assert.True(scopeFactory.SaveChangesCount >= 2);
    }
}
