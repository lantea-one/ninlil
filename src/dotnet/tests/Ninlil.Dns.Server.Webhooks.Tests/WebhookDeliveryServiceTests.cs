using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Webhooks;
using Ninlil.Dns.Server.Webhooks.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Webhooks.Tests;

/// <summary>
///     This class tests <see cref="WebhookDeliveryService" /> against a local Kestrel server that
///     acts as the webhook receiver and records each request, as <c>CloudflareDnsProviderTests</c>
///     does for the cloud connectors. Subscriptions and deliveries live in in-memory fakes. It
///     covers the request body, auth headers and HMAC signature, event-type and disabled
///     filtering, and the retry state machine, with retries driven by calling
///     <see cref="WebhookDeliveryService.ResumeAttemptAsync" /> directly.
/// </summary>
public sealed class WebhookDeliveryServiceTests : IAsyncLifetime
{
    private WebApplication _fakeServer;
    private readonly List<CapturedRequest> _capturedRequests = [];

    /// <summary>
    ///     This field holds the <see cref="WebhookCredentialProtector" /> the tests encrypt
    ///     credentials with and <see cref="WebhookDeliveryService" /> decrypts them with. It must
    ///     be one instance: each <see cref="EphemeralDataProtectionProvider" /> has its own random
    ///     key, so one instance cannot decrypt what another encrypted.
    /// </summary>
    private WebhookCredentialProtector _credentialProtector;

    /// <summary>
    ///     This field controls how many requests the fake server answers with <c>500</c> before
    ///     it starts succeeding; <c>0</c> means every request succeeds.
    /// </summary>
    private int _failuresBeforeSuccess;
    private int _requestCount;

    public async Task InitializeAsync()
    {
        _credentialProtector = new WebhookCredentialProtector(
            new EphemeralDataProtectionProvider()
        );

        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        _fakeServer = builder.Build();

        _fakeServer.MapPost(
            "/webhook",
            async (HttpContext context) =>
            {
                int requestNumber = Interlocked.Increment(ref _requestCount);

                using StreamReader reader = new(context.Request.Body);
                string body = await reader.ReadToEndAsync();

                lock (_capturedRequests)
                {
                    _capturedRequests.Add(
                        new CapturedRequest(
                            body,
                            context.Request.Headers.Authorization.ToString(),
                            context.Request.Headers["X-Ninlil-Signature"].ToString()
                        )
                    );
                }

                if (requestNumber <= _failuresBeforeSuccess)
                    return Results.StatusCode(StatusCodes.Status500InternalServerError);

                return Results.Ok();
            }
        );

        await _fakeServer.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _fakeServer.StopAsync();
        await _fakeServer.DisposeAsync();
    }

    private string TargetUrl => $"{_fakeServer.Urls.First()}/webhook";

    [Fact]
    public async Task MatchingSubscriptionReceivesPostWithCorrectBodyAuthHeaderAndHmacSignature()
    {
        const string bearerToken = "super-secret-token";
        const string hmacSecret = "hmac-shared-secret";

        WebhookSubscriptionEntity subscription = new()
        {
            Id = Guid.NewGuid(),
            Name = "matching-subscription",
            TargetUrl = TargetUrl,
            EventTypeNames = ["DnsZoneChanged"],
            AuthMode = WebhookAuthModeEnum.Bearer,
            AuthCredentialsEncrypted = _credentialProtector.ProtectAuthCredentials(
                JsonSerializer.Serialize(
                    new Dictionary<string, string> { ["BearerToken"] = bearerToken }
                )
            ),
            HmacSigningSecretEncrypted = _credentialProtector.ProtectHmacSigningSecret(hmacSecret),
            MaxDeliveryAttempts = 3,
            IsDisabled = false,
        };

        WebhookDeliveryService deliveryService = CreateDeliveryService([subscription]);

        DnsZoneChangedEvent domainEvent = new(DateTimeOffset.UtcNow, Guid.NewGuid(), "example.com");

        await deliveryService.DeliverAsync(domainEvent, CancellationToken.None);

        CapturedRequest request = Assert.Single(_capturedRequests);

        Assert.Equal($"Bearer {bearerToken}", request.AuthorizationHeader);

        byte[] expectedSignature = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(hmacSecret),
            Encoding.UTF8.GetBytes(request.Body)
        );
        Assert.Equal(Convert.ToHexStringLower(expectedSignature), request.SignatureHeader);

        using JsonDocument document = JsonDocument.Parse(request.Body);
        Assert.Equal("DnsZoneChanged", document.RootElement.GetProperty("eventType").GetString());
        Assert.Equal(
            domainEvent.ZoneId,
            document.RootElement.GetProperty("data").GetProperty("zoneId").GetGuid()
        );
        Assert.Equal(
            "example.com",
            document.RootElement.GetProperty("data").GetProperty("zoneName").GetString()
        );
    }

    [Fact]
    public async Task MatchingSubscriptionWithBasicAuthSendsTheExpectedAuthorizationHeader()
    {
        WebhookSubscriptionEntity subscription = new()
        {
            Id = Guid.NewGuid(),
            Name = "basic-auth-subscription",
            TargetUrl = TargetUrl,
            EventTypeNames = ["DnsZoneChanged"],
            AuthMode = WebhookAuthModeEnum.Basic,
            AuthCredentialsEncrypted = _credentialProtector.ProtectAuthCredentials(
                JsonSerializer.Serialize(
                    new Dictionary<string, string>
                    {
                        ["Username"] = "alice",
                        ["Password"] = "hunter2",
                    }
                )
            ),
            MaxDeliveryAttempts = 3,
        };

        WebhookDeliveryService deliveryService = CreateDeliveryService([subscription]);

        await deliveryService.DeliverAsync(
            new DnsZoneChangedEvent(DateTimeOffset.UtcNow, Guid.NewGuid(), "example.com"),
            CancellationToken.None
        );

        CapturedRequest request = Assert.Single(_capturedRequests);
        string expected =
            $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes("alice:hunter2"))}";
        Assert.Equal(expected, request.AuthorizationHeader);
    }

    [Fact]
    public async Task NonMatchingSubscriptionNeverReceivesTheEvent()
    {
        WebhookSubscriptionEntity subscription = new()
        {
            Id = Guid.NewGuid(),
            Name = "non-matching-subscription",
            TargetUrl = TargetUrl,
            EventTypeNames = ["DnsCacheEvicted"],
            AuthMode = WebhookAuthModeEnum.None,
            MaxDeliveryAttempts = 3,
        };

        WebhookDeliveryService deliveryService = CreateDeliveryService([subscription]);

        await deliveryService.DeliverAsync(
            new DnsZoneChangedEvent(DateTimeOffset.UtcNow, Guid.NewGuid(), "example.com"),
            CancellationToken.None
        );

        Assert.Empty(_capturedRequests);
    }

    [Fact]
    public async Task DisabledSubscriptionIsNeverCalled()
    {
        WebhookSubscriptionEntity enabledSubscription = new()
        {
            Id = Guid.NewGuid(),
            Name = "enabled-subscription",
            TargetUrl = TargetUrl,
            EventTypeNames = ["DnsZoneChanged"],
            IsDisabled = false,
            MaxDeliveryAttempts = 3,
        };
        WebhookSubscriptionEntity disabledSubscription = new()
        {
            Id = Guid.NewGuid(),
            Name = "disabled-subscription",
            TargetUrl = TargetUrl,
            EventTypeNames = ["DnsZoneChanged"],
            IsDisabled = true,
            MaxDeliveryAttempts = 3,
        };

        WebhookDeliveryService deliveryService = CreateDeliveryService([
            enabledSubscription,
            disabledSubscription,
        ]);

        await deliveryService.DeliverAsync(
            new DnsZoneChangedEvent(DateTimeOffset.UtcNow, Guid.NewGuid(), "example.com"),
            CancellationToken.None
        );

        // Only the enabled subscription gets a request: ListEnabledAsync filters out the
        // disabled one, as the real repositories do.
        Assert.Single(_capturedRequests);
    }

    [Fact]
    public async Task FailingEndpointIsRetriedUpToMaxDeliveryAttemptsThenDeadLetters()
    {
        _failuresBeforeSuccess = int.MaxValue;

        WebhookSubscriptionEntity subscription = new()
        {
            Id = Guid.NewGuid(),
            Name = "always-failing-subscription",
            TargetUrl = TargetUrl,
            EventTypeNames = ["DnsZoneChanged"],
            MaxDeliveryAttempts = 3,
        };

        FakeWebhookDeliveryAttemptRepository attemptRepository = new();
        WebhookDeliveryService deliveryService = CreateDeliveryService(
            [subscription],
            attemptRepository
        );

        // DeliverAsync makes the first attempt; later ones come from ResumeAttemptAsync, the
        // call the retry sweep makes once NextRetryDueUtc has passed. Calling it directly, with
        // no wait, keeps the test fast and deterministic.
        await deliveryService.DeliverAsync(
            new DnsZoneChangedEvent(DateTimeOffset.UtcNow, Guid.NewGuid(), "example.com"),
            CancellationToken.None
        );

        WebhookDeliveryAttemptEntity attempt = Assert.Single(attemptRepository.Items);
        Assert.Equal(WebhookDeliveryAttemptStatusEnum.Pending, attempt.Status);
        Assert.Equal(1, attempt.AttemptCount);

        await deliveryService.ResumeAttemptAsync(attempt, CancellationToken.None);
        Assert.Equal(WebhookDeliveryAttemptStatusEnum.Pending, attempt.Status);
        Assert.Equal(2, attempt.AttemptCount);

        await deliveryService.ResumeAttemptAsync(attempt, CancellationToken.None);
        Assert.Equal(WebhookDeliveryAttemptStatusEnum.DeadLettered, attempt.Status);
        Assert.Equal(3, attempt.AttemptCount);
        Assert.Null(attempt.NextRetryDueUtc);
        Assert.NotNull(attempt.CompletedAtUtc);

        Assert.Equal(3, _capturedRequests.Count);
    }

    [Fact]
    public async Task EndpointThatFailsOnceThenSucceedsIsDeliveredOnTheResumedRetry()
    {
        _failuresBeforeSuccess = 1;

        WebhookSubscriptionEntity subscription = new()
        {
            Id = Guid.NewGuid(),
            Name = "eventually-succeeding-subscription",
            TargetUrl = TargetUrl,
            EventTypeNames = ["DnsZoneChanged"],
            MaxDeliveryAttempts = 5,
        };

        FakeWebhookDeliveryAttemptRepository attemptRepository = new();
        WebhookDeliveryService deliveryService = CreateDeliveryService(
            [subscription],
            attemptRepository
        );

        await deliveryService.DeliverAsync(
            new DnsZoneChangedEvent(DateTimeOffset.UtcNow, Guid.NewGuid(), "example.com"),
            CancellationToken.None
        );

        WebhookDeliveryAttemptEntity attempt = Assert.Single(attemptRepository.Items);
        Assert.Equal(WebhookDeliveryAttemptStatusEnum.Pending, attempt.Status);

        await deliveryService.ResumeAttemptAsync(attempt, CancellationToken.None);

        // One failed attempt, then a successful retry; MaxDeliveryAttempts (5) is not reached.
        Assert.Equal(2, _capturedRequests.Count);
        Assert.Equal(WebhookDeliveryAttemptStatusEnum.Succeeded, attempt.Status);
        Assert.Equal(2, attempt.AttemptCount);
        Assert.Null(attempt.NextRetryDueUtc);
        Assert.NotNull(attempt.CompletedAtUtc);
    }

    [Fact]
    public async Task ResumeAttemptAsync_ForADeletedSubscription_DeadLettersImmediatelyWithoutAttempting()
    {
        WebhookSubscriptionEntity subscription = new()
        {
            Id = Guid.NewGuid(),
            Name = "soon-to-be-deleted-subscription",
            TargetUrl = TargetUrl,
            EventTypeNames = ["DnsZoneChanged"],
            MaxDeliveryAttempts = 5,
        };

        FakeWebhookDeliveryAttemptRepository attemptRepository = new();
        FakeWebhookSubscriptionRepository subscriptionRepository = new([subscription]);

        WebhookDeliveryService deliveryService = new(
            subscriptionRepository,
            attemptRepository,
            new FakeUnitOfWork(),
            _credentialProtector,
            BuildHttpClientFactory(),
            new StaticOptionsMonitor<WebhookDeliveryOptions>(
                new WebhookDeliveryOptions
                {
                    InitialRetryDelay = TimeSpan.FromMilliseconds(10),
                    MaxRetryDelay = TimeSpan.FromMilliseconds(50),
                    RequestTimeout = TimeSpan.FromSeconds(5),
                }
            ),
            NullLogger<WebhookDeliveryService>.Instance
        );

        await deliveryService.DeliverAsync(
            new DnsZoneChangedEvent(DateTimeOffset.UtcNow, Guid.NewGuid(), "example.com"),
            CancellationToken.None
        );

        WebhookDeliveryAttemptEntity attempt = Assert.Single(attemptRepository.Items);
        int requestsBeforeResume = _capturedRequests.Count;

        // FakeWebhookSubscriptionRepository is read-only, so the delete is simulated with an
        // empty repository whose GetByIdAsync finds nothing, as after a real delete.
        subscriptionRepository = new FakeWebhookSubscriptionRepository([]);
        deliveryService = new WebhookDeliveryService(
            subscriptionRepository,
            attemptRepository,
            new FakeUnitOfWork(),
            _credentialProtector,
            BuildHttpClientFactory(),
            new StaticOptionsMonitor<WebhookDeliveryOptions>(
                new WebhookDeliveryOptions
                {
                    InitialRetryDelay = TimeSpan.FromMilliseconds(10),
                    MaxRetryDelay = TimeSpan.FromMilliseconds(50),
                    RequestTimeout = TimeSpan.FromSeconds(5),
                }
            ),
            NullLogger<WebhookDeliveryService>.Instance
        );

        await deliveryService.ResumeAttemptAsync(attempt, CancellationToken.None);

        Assert.Equal(WebhookDeliveryAttemptStatusEnum.DeadLettered, attempt.Status);
        Assert.Null(attempt.NextRetryDueUtc);
        Assert.Contains("no longer exists", attempt.LastErrorMessage);

        // No request was sent to the deleted subscription's (still reachable) target URL.
        Assert.Equal(requestsBeforeResume, _capturedRequests.Count);
    }

    [Fact]
    public async Task ResumeAttemptAsync_ForADisabledSubscription_ReschedulesWithoutAttempting()
    {
        // The first attempt must fail, or the delivery would already be Succeeded when the
        // subscription is disabled below.
        _failuresBeforeSuccess = int.MaxValue;

        WebhookSubscriptionEntity subscription = new()
        {
            Id = Guid.NewGuid(),
            Name = "will-be-disabled-subscription",
            TargetUrl = TargetUrl,
            EventTypeNames = ["DnsZoneChanged"],
            MaxDeliveryAttempts = 5,
            IsDisabled = false,
        };

        FakeWebhookDeliveryAttemptRepository attemptRepository = new();
        WebhookDeliveryService deliveryService = CreateDeliveryService(
            [subscription],
            attemptRepository
        );

        await deliveryService.DeliverAsync(
            new DnsZoneChangedEvent(DateTimeOffset.UtcNow, Guid.NewGuid(), "example.com"),
            CancellationToken.None
        );

        WebhookDeliveryAttemptEntity attempt = Assert.Single(attemptRepository.Items);
        int attemptCountBeforeResume = attempt.AttemptCount;
        int requestsBeforeResume = _capturedRequests.Count;
        DateTimeOffset nextRetryDueBeforeResume = attempt.NextRetryDueUtc!.Value;

        subscription.IsDisabled = true;

        await deliveryService.ResumeAttemptAsync(attempt, CancellationToken.None);

        // Deferred: no attempt spent, no request made, still Pending and due again later.
        Assert.Equal(attemptCountBeforeResume, attempt.AttemptCount);
        Assert.Equal(requestsBeforeResume, _capturedRequests.Count);
        Assert.Equal(WebhookDeliveryAttemptStatusEnum.Pending, attempt.Status);
        Assert.True(attempt.NextRetryDueUtc > nextRetryDueBeforeResume);
    }

    /// <summary>
    ///     This method builds a <see cref="WebhookDeliveryService" /> with an
    ///     <see cref="IHttpClientFactory" /> from <see cref="BuildHttpClientFactory" />, fixed
    ///     short retry delays, and an in-memory <see cref="IWebhookSubscriptionRepository" />
    ///     holding <paramref name="subscriptions" />.
    /// </summary>
    private WebhookDeliveryService CreateDeliveryService(
        IReadOnlyList<WebhookSubscriptionEntity> subscriptions,
        IWebhookDeliveryAttemptRepository attemptRepository = null,
        TimeSpan? initialRetryDelay = null,
        TimeSpan? maxRetryDelay = null
    )
    {
        WebhookDeliveryOptions deliveryOptions = new()
        {
            InitialRetryDelay = initialRetryDelay ?? TimeSpan.FromMilliseconds(20),
            MaxRetryDelay = maxRetryDelay ?? TimeSpan.FromMilliseconds(50),
            RequestTimeout = TimeSpan.FromSeconds(5),
        };

        return new WebhookDeliveryService(
            new FakeWebhookSubscriptionRepository(subscriptions),
            attemptRepository ?? new FakeWebhookDeliveryAttemptRepository(),
            new FakeUnitOfWork(),
            _credentialProtector,
            BuildHttpClientFactory(),
            new StaticOptionsMonitor<WebhookDeliveryOptions>(deliveryOptions),
            NullLogger<WebhookDeliveryService>.Instance
        );
    }

    /// <summary>
    ///     This method builds an <see cref="IHttpClientFactory" /> from a small
    ///     <see cref="ServiceCollection" />, registering the named client as production does
    ///     but without the timeout callback.
    /// </summary>
    private static IHttpClientFactory BuildHttpClientFactory()
    {
        ServiceCollection services = new();
        services.AddHttpClient(WebhookDeliveryService.HttpClientName);
        return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
    }

    private sealed record CapturedRequest(
        string Body,
        string AuthorizationHeader,
        string SignatureHeader
    );
}
