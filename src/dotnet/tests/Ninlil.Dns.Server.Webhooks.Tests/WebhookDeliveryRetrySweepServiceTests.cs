using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Webhooks.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Webhooks.Tests;

/// <summary>
///     This class tests <see cref="WebhookDeliveryRetrySweepService" /> on a SQLite-backed
///     <see cref="WebhookTestHost" /> with a local Kestrel server as the webhook receiver. Each
///     test calls <see cref="WebhookDeliveryRetrySweepService.SweepAsync" /> once instead of
///     waiting for <see cref="WebhookDeliveryOptions.SweepInterval" />. It covers resuming a
///     due delivery to success or dead letter, three due rows resumed in parallel on one
///     context, the service loop surviving a failing sweep, and the retention query that removes old
///     completed rows.
/// </summary>
public sealed class WebhookDeliveryRetrySweepServiceTests : IAsyncLifetime
{
    private readonly WebhookTestHost _testHost = new();
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-webhook-sweep-tests-{Guid.NewGuid()}.sqlite3"
    );
    private WebApplication _fakeServer;
    private int _failuresBeforeSuccess;
    private int _barrierArrivals;
    private int _barrierConcurrent;
    private int _barrierMaxConcurrent;
    private readonly TaskCompletionSource _barrierOpen = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    public async Task InitializeAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        _fakeServer = builder.Build();

        int requestCount = 0;
        _fakeServer.MapPost(
            "/webhook",
            (HttpContext context) =>
            {
                int requestNumber = Interlocked.Increment(ref requestCount);
                return requestNumber <= _failuresBeforeSuccess
                    ? Results.StatusCode(StatusCodes.Status500InternalServerError)
                    : Results.Ok();
            }
        );

        // Holds each response until three requests are in flight, which proves that the sweep
        // sends them in parallel. If they were sent one by one the third would never arrive, so
        // the wait times out and the test fails on its concurrency assertion.
        _fakeServer.MapPost(
            "/barrier",
            async () =>
            {
                int concurrent = Interlocked.Increment(ref _barrierConcurrent);
                int seen;
                do
                {
                    seen = Volatile.Read(ref _barrierMaxConcurrent);
                } while (
                    concurrent > seen
                    && Interlocked.CompareExchange(ref _barrierMaxConcurrent, concurrent, seen)
                        != seen
                );

                if (Interlocked.Increment(ref _barrierArrivals) >= 3)
                    _barrierOpen.TrySetResult();

                await Task.WhenAny(_barrierOpen.Task, Task.Delay(TimeSpan.FromSeconds(3)));
                Interlocked.Decrement(ref _barrierConcurrent);
                return Results.Ok();
            }
        );

        await _fakeServer.StartAsync();

        await _testHost.InitializeAsync(
            _databasePath,
            initialRetryDelay: TimeSpan.FromMilliseconds(1),
            maxRetryDelay: TimeSpan.FromMilliseconds(5),
            sweepInterval: TimeSpan.FromHours(1) // Unused: the tests call SweepAsync directly.
        );
    }

    public async Task DisposeAsync()
    {
        await _fakeServer.StopAsync();
        await _fakeServer.DisposeAsync();
        await _testHost.DisposeAsync();

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    private string TargetUrl => $"{_fakeServer.Urls.First()}/webhook";

    [Fact]
    public async Task SweepAsync_ResumesADueDeliveryAndAdvancesItToSuccess()
    {
        // A sweep makes one attempt per due delivery, and the seeded first attempt made no
        // request (see SeedFirstAttemptAsync), so the one request this sweep sends must succeed.
        _failuresBeforeSuccess = 0;

        Guid attemptId = await SeedFirstAttemptAsync("resume-to-success", MaxDeliveryAttempts: 5);

        WebhookDeliveryRetrySweepService sweepService = GetSweepService();

        // The seeded attempt is already Pending with an elapsed NextRetryDueUtc (see
        // SeedFirstAttemptAsync), so this single sweep pass finds and resumes it.
        (int resumed, _) = await sweepService.SweepAsync();
        Assert.Equal(1, resumed);

        WebhookDeliveryAttemptEntity attempt = await GetAttemptAsync(attemptId);
        Assert.Equal(WebhookDeliveryAttemptStatusEnum.Succeeded, attempt.Status);
        Assert.Equal(2, attempt.AttemptCount);
    }

    [Fact]
    public async Task SweepAsync_ResumesADueDeliveryThroughToDeadLetter()
    {
        _failuresBeforeSuccess = int.MaxValue;

        Guid attemptId = await SeedFirstAttemptAsync(
            "resume-to-dead-letter",
            MaxDeliveryAttempts: 2
        );

        WebhookDeliveryRetrySweepService sweepService = GetSweepService();

        (int resumed, _) = await sweepService.SweepAsync();
        Assert.Equal(1, resumed);

        WebhookDeliveryAttemptEntity attempt = await GetAttemptAsync(attemptId);
        Assert.Equal(WebhookDeliveryAttemptStatusEnum.DeadLettered, attempt.Status);
        Assert.Equal(2, attempt.AttemptCount);
    }

    [Fact]
    public async Task SweepAsync_WithThreeDueRowsOfThreeSubscriptions_ResumesThemInParallelAndSavesAll()
    {
        List<Guid> attemptIds = [];
        for (int index = 0; index < 3; index++)
            attemptIds.Add(
                await SeedFirstAttemptAsync($"parallel-{index}", MaxDeliveryAttempts: 5, "/barrier")
            );

        (int resumed, _) = await GetSweepService().SweepAsync();

        Assert.Equal(3, resumed);
        Assert.Equal(3, _barrierMaxConcurrent);
        foreach (Guid attemptId in attemptIds)
        {
            WebhookDeliveryAttemptEntity attempt = await GetAttemptAsync(attemptId);
            Assert.Equal(WebhookDeliveryAttemptStatusEnum.Succeeded, attempt.Status);
            Assert.Equal(2, attempt.AttemptCount);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenEverySweepThrows_WaitsTheIntervalAndWarnsOncePerSweep()
    {
        ThrowingScopeFactory scopeFactory = new();
        CapturingLogger<WebhookDeliveryRetrySweepService> logger = new();
        WebhookDeliveryRetrySweepService service = new(
            scopeFactory,
            new StaticOptionsMonitor<WebhookDeliveryOptions>(
                new WebhookDeliveryOptions { SweepInterval = TimeSpan.FromMilliseconds(200) }
            ),
            logger
        );

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(1));
        await service.StopAsync(CancellationToken.None);

        Assert.InRange(scopeFactory.Calls, 3, 7);
        Assert.Equal(
            scopeFactory.Calls,
            logger.Entries.Count(entry => entry.Level == LogLevel.Warning)
        );
    }

    [Fact]
    public async Task ExecuteAsync_WithAZeroSweepInterval_WaitsTheDefaultAndWarnsOnceAboutIt()
    {
        ThrowingScopeFactory scopeFactory = new();
        CapturingLogger<WebhookDeliveryRetrySweepService> logger = new();
        WebhookDeliveryRetrySweepService service = new(
            scopeFactory,
            new StaticOptionsMonitor<WebhookDeliveryOptions>(
                new WebhookDeliveryOptions { SweepInterval = TimeSpan.Zero }
            ),
            logger
        );

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(1));
        await service.StopAsync(CancellationToken.None);

        Assert.InRange(scopeFactory.Calls, 1, 2);
        Assert.Single(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning && entry.Message.Contains("SweepInterval")
        );
    }

    [Fact]
    public async Task SweepAsync_RemovesTerminalDeliveriesPastTheRetentionPeriodButNeverAPendingOne()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await using (AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope())
        {
            IWebhookDeliveryAttemptRepository repository =
                scope.ServiceProvider.GetRequiredService<IWebhookDeliveryAttemptRepository>();
            IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            // An old Succeeded row, past a one-hour retention window.
            await repository.AddAsync(
                new WebhookDeliveryAttemptEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = now.AddHours(-3),
                    SubscriptionId = Guid.NewGuid(),
                    SubscriptionName = "old-succeeded",
                    EventTypeName = "DnsZoneChanged",
                    PayloadJson = "{}",
                    Status = WebhookDeliveryAttemptStatusEnum.Succeeded,
                    AttemptCount = 1,
                    MaxAttempts = 5,
                    LastAttemptedAtUtc = now.AddHours(-3),
                    CompletedAtUtc = now.AddHours(-3),
                }
            );

            // A recent DeadLettered row, inside the retention window, which must stay.
            await repository.AddAsync(
                new WebhookDeliveryAttemptEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = now.AddMinutes(-5),
                    SubscriptionId = Guid.NewGuid(),
                    SubscriptionName = "recent-dead-lettered",
                    EventTypeName = "DnsZoneChanged",
                    PayloadJson = "{}",
                    Status = WebhookDeliveryAttemptStatusEnum.DeadLettered,
                    AttemptCount = 3,
                    MaxAttempts = 3,
                    LastAttemptedAtUtc = now.AddMinutes(-5),
                    CompletedAtUtc = now.AddMinutes(-5),
                }
            );

            // An old Pending row, which must stay whatever its age, since it may still be retried.
            await repository.AddAsync(
                new WebhookDeliveryAttemptEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = now.AddHours(-3),
                    SubscriptionId = Guid.NewGuid(),
                    SubscriptionName = "old-but-still-pending",
                    EventTypeName = "DnsZoneChanged",
                    PayloadJson = "{}",
                    Status = WebhookDeliveryAttemptStatusEnum.Pending,
                    AttemptCount = 1,
                    MaxAttempts = 5,
                    LastAttemptedAtUtc = now.AddHours(-3),
                    NextRetryDueUtc = now.AddHours(100), // Far in the future, so never due.
                }
            );

            await unitOfWork.SaveChangesAsync();
        }

        // SweepAsync calls this repository method with a cutoff from AttemptRetentionPeriod.
        // The test calls it directly with a one-hour cutoff instead of building a host with that
        // retention period, as DnsQueryLogCleanupServiceTests does for the query log.
        await using AsyncServiceScope verifyScope = _testHost.Host.Services.CreateAsyncScope();
        IWebhookDeliveryAttemptRepository verifyRepository =
            verifyScope.ServiceProvider.GetRequiredService<IWebhookDeliveryAttemptRepository>();

        int removed = await verifyRepository.DeleteCompletedOlderThanAsync(now.AddHours(-1));
        Assert.Equal(1, removed);

        IReadOnlyList<WebhookDeliveryAttemptEntity> remaining = await verifyRepository.ListAsync();
        Assert.Equal(2, remaining.Count);
        Assert.Contains(remaining, item => item.SubscriptionName == "recent-dead-lettered");
        Assert.Contains(remaining, item => item.SubscriptionName == "old-but-still-pending");
    }

    /// <summary>
    ///     This method saves a subscription and a delivery whose first attempt failed, without
    ///     calling <see cref="WebhookDeliveryService.DeliverAsync" /> or sending a request: the row
    ///     is <see cref="WebhookDeliveryAttemptStatusEnum.Pending" /> with a
    ///     <see cref="WebhookDeliveryAttemptEntity.NextRetryDueUtc" /> already passed, as a delivery
    ///     waiting across a restart would be. The next <see cref="WebhookDeliveryRetrySweepService.SweepAsync" />
    ///     picks it up without any waiting.
    /// </summary>
    private async Task<Guid> SeedFirstAttemptAsync(
        string subscriptionName,
        int MaxDeliveryAttempts,
        string path = "/webhook"
    )
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IWebhookSubscriptionRepository subscriptionRepository =
            scope.ServiceProvider.GetRequiredService<IWebhookSubscriptionRepository>();
        IWebhookDeliveryAttemptRepository attemptRepository =
            scope.ServiceProvider.GetRequiredService<IWebhookDeliveryAttemptRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        WebhookSubscriptionEntity subscription = new()
        {
            Id = Guid.NewGuid(),
            Name = subscriptionName,
            TargetUrl = $"{_fakeServer.Urls.First()}{path}",
            EventTypeNames = ["DnsZoneChanged"],
            MaxDeliveryAttempts = MaxDeliveryAttempts,
        };
        await subscriptionRepository.AddAsync(subscription);

        Guid attemptId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await attemptRepository.AddAsync(
            new WebhookDeliveryAttemptEntity
            {
                Id = attemptId,
                CreatedAtUtc = now,
                SubscriptionId = subscription.Id,
                SubscriptionName = subscription.Name,
                EventTypeName = "DnsZoneChanged",
                PayloadJson = """{"eventType":"DnsZoneChanged","data":{}}""",
                Status = WebhookDeliveryAttemptStatusEnum.Pending,
                AttemptCount = 1,
                MaxAttempts = MaxDeliveryAttempts,
                LastAttemptedAtUtc = now,
                LastHttpStatusCode = 500,
                LastErrorMessage = "received non-success status 500",
                NextRetryDueUtc = now.AddMilliseconds(-1), // Already due.
            }
        );

        await unitOfWork.SaveChangesAsync();
        return attemptId;
    }

    private async Task<WebhookDeliveryAttemptEntity> GetAttemptAsync(Guid attemptId)
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IWebhookDeliveryAttemptRepository repository =
            scope.ServiceProvider.GetRequiredService<IWebhookDeliveryAttemptRepository>();
        return await repository.GetByIdAsync(attemptId);
    }

    /// <summary>
    ///     This method gets the <see cref="WebhookDeliveryRetrySweepService" /> instance the host
    ///     registered with <c>AddHostedService&lt;T&gt;()</c>. That registration resolves only as
    ///     <see cref="IHostedService" />, so it is found among those, as
    ///     <c>ManagedServiceRegistry.Build</c> in <c>Ninlil.Web</c> does.
    /// </summary>
    private WebhookDeliveryRetrySweepService GetSweepService() =>
        _testHost
            .Host.Services.GetServices<IHostedService>()
            .OfType<WebhookDeliveryRetrySweepService>()
            .Single();

    /// <summary>
    ///     This class is an <see cref="IServiceScopeFactory" /> whose every scope request throws,
    ///     which makes each sweep fail before it reads anything, and counts the requests.
    /// </summary>
    private sealed class ThrowingScopeFactory : IServiceScopeFactory
    {
        private int _calls;

        /// <summary>The number of sweeps that tried to start.</summary>
        public int Calls => Volatile.Read(ref _calls);

        /// <inheritdoc />
        public IServiceScope CreateScope()
        {
            Interlocked.Increment(ref _calls);
            throw new InvalidOperationException("The sweep always fails.");
        }
    }
}
