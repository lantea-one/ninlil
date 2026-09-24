using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.Webhooks.Tests;

/// <summary>
///     This class covers <see cref="WebhookDeliveryRetrySweepService.SweepAsync" /> directly
///     (driven once per test rather than waiting out a real <see cref="WebhookDeliveryOptions.SweepInterval" />)
///     against a real, SQLite-backed <see cref="WebhookTestHost" />: resuming a due delivery to
///     completion, and the retention sweep's own bulk removal of old, terminal rows.
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

        await _fakeServer.StartAsync();

        await _testHost.InitializeAsync(
            _databasePath,
            initialRetryDelay: TimeSpan.FromMilliseconds(1),
            maxRetryDelay: TimeSpan.FromMilliseconds(5),
            sweepInterval: TimeSpan.FromHours(1) // Not exercised here -- this test drives SweepAsync directly.
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
        // ResumeAttemptAsync performs exactly one real HTTP attempt per sweep pass (by design --
        // see WebhookDeliveryRetrySweepService's own doc comment); this test drives exactly one
        // SweepAsync() call, so that one real request (the seeded first attempt was faked,
        // bypassing HTTP entirely -- see SeedFirstAttemptAsync's own doc comment) must itself
        // succeed for this delivery to resolve within this single pass.
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
    public async Task SweepAsync_RemovesTerminalDeliveriesPastTheRetentionPeriodButNeverAPendingOne()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await using (AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope())
        {
            IWebhookDeliveryAttemptRepository repository =
                scope.ServiceProvider.GetRequiredService<IWebhookDeliveryAttemptRepository>();
            IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            // An old, terminal (Succeeded) row -- past a 1-hour retention window.
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

            // A recent, terminal (DeadLettered) row -- inside the retention window, must survive.
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

            // An old row still genuinely Pending -- must never be removed by the retention sweep
            // regardless of age, since it may still be retried.
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
                    NextRetryDueUtc = now.AddHours(100), // Far in the future -- never picked up by this test's own sweep.
                }
            );

            await unitOfWork.SaveChangesAsync();
        }

        // This repository method itself is what WebhookDeliveryRetrySweepService.SweepAsync
        // calls every sweep pass, governed by WebhookDeliveryOptions.AttemptRetentionPeriod --
        // exercised here directly with an explicit 1-hour cutoff, the same "drive the underlying
        // repository call with an explicit value rather than rebuilding a whole host per
        // retention window" shape DnsQueryLogCleanupServiceTests already establishes for the
        // query log's own comparable retention sweep.
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
    ///     This method seeds a delivery's own already-completed first attempt directly (bypassing
    ///     <see cref="WebhookDeliveryService.DeliverAsync" />'s own live HTTP call), landing it as
    ///     <see cref="WebhookDeliveryAttemptStatusEnum.Pending" /> with an already-elapsed
    ///     <see cref="WebhookDeliveryAttemptEntity.NextRetryDueUtc" /> -- exactly the shape a
    ///     genuinely interrupted, restart-surviving delivery has on disk, which is what this
    ///     class's own sweep tests need to drive <see cref="WebhookDeliveryRetrySweepService.SweepAsync" />
    ///     against without a real first HTTP round trip or a real elapsed wait.
    /// </summary>
    private async Task<Guid> SeedFirstAttemptAsync(string subscriptionName, int MaxDeliveryAttempts)
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
            TargetUrl = TargetUrl,
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
    ///     This method reaches the real, already-constructed <see cref="WebhookDeliveryRetrySweepService" />
    ///     singleton <c>AddHostedService&lt;T&gt;()</c> registered against this host's own root
    ///     service provider -- that registration only ever resolves as <see cref="IHostedService" />,
    ///     never by its own concrete type directly, so this method finds it the same way
    ///     <c>Ninlil.Web.Services.ManagedServiceRegistry.Build</c>'s own <c>Resolve&lt;T&gt;</c>
    ///     local function already does for every other hosted service in this project.
    /// </summary>
    private WebhookDeliveryRetrySweepService GetSweepService() =>
        _testHost
            .Host.Services.GetServices<IHostedService>()
            .OfType<WebhookDeliveryRetrySweepService>()
            .Single();
}
