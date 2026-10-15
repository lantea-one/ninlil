using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
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
///     This class tests <see cref="WebhookDeliveryService.DeliverAsync" /> and the retry sweep on
///     a SQLite-backed <see cref="WebhookTestHost" />: one real EF Core context per scope, so two
///     operations that overlap on it would fail like they do in production. A local Kestrel
///     server is the receiver and counts the requests it gets. It covers several matching
///     subscriptions for one event, and a subscription whose credential or signing secret was
///     encrypted with another key ring (an <see cref="EphemeralDataProtectionProvider" /> that
///     is not the host's), which must dead-letter without sending a request.
/// </summary>
public sealed class WebhookDeliverySqliteTests : IAsyncLifetime
{
    private const string UnreadableMessage = "cannot be decrypted";

    private readonly WebhookTestHost _testHost = new();
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-webhook-sqlite-tests-{Guid.NewGuid()}.sqlite3"
    );

    /// <summary>
    ///     This protector encrypts with a key ring the host does not have, so the host's
    ///     <see cref="WebhookCredentialProtector" /> cannot decrypt what it wrote.
    /// </summary>
    private readonly WebhookCredentialProtector _foreignProtector = new(
        new EphemeralDataProtectionProvider()
    );
    private WebApplication _fakeServer;
    private int _requestCount;

    public async Task InitializeAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        _fakeServer = builder.Build();
        _fakeServer.MapPost(
            "/webhook",
            () =>
            {
                Interlocked.Increment(ref _requestCount);
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
    public async Task DeliverAsync_WithTwoMatchingSubscriptions_SavesTwoAttemptRows()
    {
        Guid zoneId = Guid.NewGuid();
        Guid first = await SeedSubscriptionAsync("two-a");
        Guid second = await SeedSubscriptionAsync("two-b");

        await DeliverAsync(zoneId);

        IReadOnlyList<WebhookDeliveryAttemptEntity> rows = await ListAttemptsAsync();
        Assert.Contains(rows, row => row.SubscriptionId == first);
        Assert.Contains(rows, row => row.SubscriptionId == second);
        Assert.All(
            rows,
            row => Assert.Equal(WebhookDeliveryAttemptStatusEnum.Succeeded, row.Status)
        );
        Assert.Equal(2, rows.Count);
        Assert.Equal(2, _requestCount);
    }

    [Fact]
    public async Task DeliverAsync_WithAnUnreadableCredential_SavesADeadLetteredRowAndSendsNothing()
    {
        Guid id = await SeedSubscriptionAsync(
            "unreadable-credential",
            authCredentialsEncrypted: _foreignProtector.ProtectAuthCredentials(
                """{"BearerToken":"x"}"""
            ),
            authMode: WebhookAuthModeEnum.Bearer
        );

        await DeliverAsync(Guid.NewGuid());

        WebhookDeliveryAttemptEntity row = Assert.Single(await ListAttemptsAsync());
        Assert.Equal(id, row.SubscriptionId);
        Assert.Equal(WebhookDeliveryAttemptStatusEnum.DeadLettered, row.Status);
        Assert.Contains(UnreadableMessage, row.LastErrorMessage);
        Assert.Null(row.NextRetryDueUtc);
        Assert.Equal(0, _requestCount);
    }

    [Fact]
    public async Task DeliverAsync_WithAnUnreadableSigningSecret_SavesADeadLetteredRowAndSendsNothing()
    {
        await SeedSubscriptionAsync(
            "unreadable-secret",
            hmacSigningSecretEncrypted: _foreignProtector.ProtectHmacSigningSecret("secret")
        );

        await DeliverAsync(Guid.NewGuid());

        WebhookDeliveryAttemptEntity row = Assert.Single(await ListAttemptsAsync());
        Assert.Equal(WebhookDeliveryAttemptStatusEnum.DeadLettered, row.Status);
        Assert.Contains(UnreadableMessage, row.LastErrorMessage);
        Assert.Equal(0, _requestCount);
    }

    [Fact]
    public async Task SweepAsync_ForAPendingRowWithAnUnreadableCredential_DeadLettersItOnceAndFindsNothingNext()
    {
        Guid subscriptionId = await SeedSubscriptionAsync(
            "pending-unreadable",
            authCredentialsEncrypted: _foreignProtector.ProtectAuthCredentials(
                """{"BearerToken":"x"}"""
            ),
            authMode: WebhookAuthModeEnum.Bearer
        );
        Guid attemptId = await SeedDueRowAsync(subscriptionId);

        WebhookDeliveryRetrySweepService sweep = GetSweepService();
        (int first, _) = await sweep.SweepAsync();
        (int second, _) = await sweep.SweepAsync();

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        WebhookDeliveryAttemptEntity row = await GetAttemptAsync(attemptId);
        Assert.Equal(WebhookDeliveryAttemptStatusEnum.DeadLettered, row.Status);
        Assert.Contains(UnreadableMessage, row.LastErrorMessage);
        Assert.Equal(0, _requestCount);
    }

    [Fact]
    public async Task SweepAsync_ForAPendingRowWithAnUnreadableSigningSecret_DeadLettersItOnceAndFindsNothingNext()
    {
        Guid subscriptionId = await SeedSubscriptionAsync(
            "pending-unreadable-secret",
            hmacSigningSecretEncrypted: _foreignProtector.ProtectHmacSigningSecret("secret")
        );
        Guid attemptId = await SeedDueRowAsync(subscriptionId);

        WebhookDeliveryRetrySweepService sweep = GetSweepService();
        (int first, _) = await sweep.SweepAsync();
        (int second, _) = await sweep.SweepAsync();

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.Equal(
            WebhookDeliveryAttemptStatusEnum.DeadLettered,
            (await GetAttemptAsync(attemptId)).Status
        );
        Assert.Equal(0, _requestCount);
    }

    private async Task DeliverAsync(Guid zoneId)
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        WebhookDeliveryService service =
            scope.ServiceProvider.GetRequiredService<WebhookDeliveryService>();
        await service.DeliverAsync(
            new DnsZoneChangedEvent(DateTimeOffset.UtcNow, zoneId, "example.com"),
            CancellationToken.None
        );
    }

    private async Task<Guid> SeedSubscriptionAsync(
        string name,
        string authCredentialsEncrypted = null,
        WebhookAuthModeEnum authMode = WebhookAuthModeEnum.None,
        string hmacSigningSecretEncrypted = null
    )
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IWebhookSubscriptionRepository repository =
            scope.ServiceProvider.GetRequiredService<IWebhookSubscriptionRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        WebhookSubscriptionEntity subscription = new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            TargetUrl = TargetUrl,
            EventTypeNames = ["DnsZoneChanged"],
            MaxDeliveryAttempts = 5,
            AuthMode = authMode,
            AuthCredentialsEncrypted = authCredentialsEncrypted,
            HmacSigningSecretEncrypted = hmacSigningSecretEncrypted,
        };
        await repository.AddAsync(subscription);
        await unitOfWork.SaveChangesAsync();
        return subscription.Id;
    }

    private async Task<Guid> SeedDueRowAsync(Guid subscriptionId)
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IWebhookDeliveryAttemptRepository repository =
            scope.ServiceProvider.GetRequiredService<IWebhookDeliveryAttemptRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid attemptId = Guid.NewGuid();
        await repository.AddAsync(
            new WebhookDeliveryAttemptEntity
            {
                Id = attemptId,
                CreatedAtUtc = now,
                SubscriptionId = subscriptionId,
                SubscriptionName = "seeded",
                EventTypeName = "DnsZoneChanged",
                PayloadJson = """{"eventType":"DnsZoneChanged","data":{}}""",
                Status = WebhookDeliveryAttemptStatusEnum.Pending,
                AttemptCount = 1,
                MaxAttempts = 5,
                LastAttemptedAtUtc = now,
                NextRetryDueUtc = now.AddMilliseconds(-1),
            }
        );
        await unitOfWork.SaveChangesAsync();
        return attemptId;
    }

    private async Task<IReadOnlyList<WebhookDeliveryAttemptEntity>> ListAttemptsAsync()
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<IWebhookDeliveryAttemptRepository>()
            .ListAsync();
    }

    private async Task<WebhookDeliveryAttemptEntity> GetAttemptAsync(Guid attemptId)
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<IWebhookDeliveryAttemptRepository>()
            .GetByIdAsync(attemptId);
    }

    private WebhookDeliveryRetrySweepService GetSweepService() =>
        _testHost
            .Host.Services.GetServices<IHostedService>()
            .OfType<WebhookDeliveryRetrySweepService>()
            .Single();
}
