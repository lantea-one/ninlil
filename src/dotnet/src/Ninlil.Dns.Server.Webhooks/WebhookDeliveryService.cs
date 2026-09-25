using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Events.Broker;

// Define our namespace.
namespace Ninlil.Dns.Server.Webhooks;

/// <summary>
///     This class is the outbound webhook delivery engine. For an <see cref="IDnsServerEvent" />
///     it finds every enabled <see cref="WebhookSubscriptionEntity" /> whose <c>EventTypeNames</c>
///     include the event's <see cref="DnsServerEventTypeRegistry" /> name and POSTs a JSON
///     payload to each subscription's <c>TargetUrl</c>, with the configured bearer or basic
///     <c>Authorization</c> header and, when a signing secret is set, a
///     <see cref="SignatureHeaderName" /> header over the request body.
///     <para>
///         <b>Only the first attempt of a delivery is made from <see cref="DeliverAsync" />.</b>
///         It is made at once, which keeps delivery fast when the endpoint is up, and its outcome
///         is then saved as a <see cref="WebhookDeliveryAttemptEntity" />. Every later attempt is
///         made by <see cref="WebhookDeliveryRetrySweepService" /> through
///         <see cref="ResumeAttemptAsync" /> when the saved <c>NextRetryDueUtc</c> is reached;
///         nothing here waits out a backoff in memory. So a pending retry, and the wait before it,
///         survive a restart. Nothing is saved before the first attempt completes (see
///         <see cref="DeliverToSubscriptionAsync" /> for that gap).
///     </para>
///     <para>
///         This class is scoped because its repositories are. The event dispatcher never calls it
///         directly: <see cref="WebhookDeliverySubscriber{TEvent}" />, registered for each
///         webhook event type, calls it on a detached task in a new scope, so a slow endpoint
///         does not hold up other subscribers.
///     </para>
/// </summary>
/// <param name="subscriptionRepository">The repository this engine reads enabled subscriptions from.</param>
/// <param name="attemptRepository">The repository this engine saves each delivery's progress to.</param>
/// <param name="unitOfWork">The unit of work this engine commits delivery changes through.</param>
/// <param name="credentialProtector">The protector this engine decrypts a subscription's auth credentials and HMAC signing secret with.</param>
/// <param name="httpClientFactory">The factory this engine creates its outbound <see cref="HttpClient" /> from.</param>
/// <param name="options">
///     The retry and backoff settings, read through <c>options.CurrentValue</c> each time they
///     are used, so a configuration change applies to the next backoff computation without a
///     restart. <see cref="WebhookDeliveryOptions.RequestTimeout" /> is applied by the
///     <c>ConfigureHttpClient</c> callback in <c>ServiceCollectionWebhooksExtension</c>, which
///     also reads the monitor, each time a client is created for an attempt.
/// </param>
/// <param name="logger">The logger this engine records delivery attempts and failures to.</param>
public sealed class WebhookDeliveryService(
    IWebhookSubscriptionRepository subscriptionRepository,
    IWebhookDeliveryAttemptRepository attemptRepository,
    IUnitOfWork unitOfWork,
    WebhookCredentialProtector credentialProtector,
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<WebhookDeliveryOptions> options,
    ILogger<WebhookDeliveryService> logger
)
{
    /// <summary>
    ///     This constant contains the name of the outbound <see cref="HttpClient" /> registered with
    ///     <see cref="IHttpClientFactory" />.
    /// </summary>
    public const string HttpClientName = "Ninlil.Dns.Server.Webhooks";

    /// <summary>
    ///     This constant contains the header that carries a subscription's HMAC-SHA256 payload
    ///     signature: the lowercase hexadecimal digest of the request body, keyed with the UTF-8
    ///     bytes of the decrypted signing secret. A receiver uses it to check that the payload came
    ///     from this server.
    /// </summary>
    public const string SignatureHeaderName = "X-Ninlil-Signature";

    /// <summary>
    ///     This field contains the JSON options every payload is serialized with: the web
    ///     defaults, so property names are camelCase like the REST API's JSON.
    /// </summary>
    private static readonly JsonSerializerOptions PayloadSerializerOptions = new(
        JsonSerializerDefaults.Web
    );

    /// <summary>
    ///     This method makes the first delivery attempt of an event to every enabled subscription
    ///     whose event-type filter matches it. The subscriptions are attempted concurrently, so a
    ///     slow or unreachable target does not delay the others.
    /// </summary>
    /// <param name="domainEvent">The event to deliver.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public async Task DeliverAsync(IDnsServerEvent domainEvent, CancellationToken stoppingToken)
    {
        string eventTypeName = DnsServerEventTypeRegistry.GetName(domainEvent);

        IReadOnlyList<WebhookSubscriptionEntity> enabledSubscriptions =
            await subscriptionRepository.ListEnabledAsync(stoppingToken);

        List<WebhookSubscriptionEntity> matchingSubscriptions =
        [
            .. enabledSubscriptions.Where(subscription =>
                subscription.EventTypeNames.Contains(eventTypeName, StringComparer.Ordinal)
            ),
        ];

        if (matchingSubscriptions.Count == 0)
            return;

        string body = BuildPayload(domainEvent, eventTypeName);

        await Task.WhenAll(
            matchingSubscriptions.Select(subscription =>
                DeliverToSubscriptionAsync(subscription, eventTypeName, body, stoppingToken)
            )
        );
    }

    /// <summary>
    ///     This method makes the first delivery attempt for one subscription and then saves the
    ///     resulting <see cref="WebhookDeliveryAttemptEntity" />: succeeded, pending with a retry
    ///     time, or dead-lettered at once when <c>MaxDeliveryAttempts</c> is <c>1</c>. HTTP
    ///     failures are recorded in the row, not thrown.
    ///     <para>
    ///         <b>Accepted gap</b>: if the process stops during this first attempt, before the row
    ///         is saved, the delivery is lost without a record. Events are not durable before this
    ///         point either (they live in an in-memory queue), so saving a row before the request
    ///         would only move the gap earlier, and would risk a duplicate delivery if the process
    ///         stopped between that write and the attempt. From the saved row on, every wait and
    ///         retry survives a restart.
    ///     </para>
    /// </summary>
    private async Task DeliverToSubscriptionAsync(
        WebhookSubscriptionEntity subscription,
        string eventTypeName,
        string body,
        CancellationToken stoppingToken
    )
    {
        int maxAttempts = Math.Max(1, subscription.MaxDeliveryAttempts);

        DeliveryAttemptOutcome outcome = await TryDeliverOnceAsync(
            subscription,
            body,
            stoppingToken
        );

        WebhookDeliveryAttemptEntity attempt = new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            SubscriptionId = subscription.Id,
            SubscriptionName = subscription.Name,
            EventTypeName = eventTypeName,
            PayloadJson = body,
            AttemptCount = 1,
            MaxAttempts = maxAttempts,
            LastAttemptedAtUtc = DateTimeOffset.UtcNow,
            LastHttpStatusCode = outcome.StatusCode,
            LastErrorMessage = outcome.ErrorMessage,
        };

        ApplyOutcome(attempt, outcome, subscription.Name);

        await attemptRepository.AddAsync(attempt, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);
    }

    /// <summary>
    ///     This method resumes one saved delivery that <see cref="WebhookDeliveryRetrySweepService" />
    ///     found due. It reads the subscription again, so a changed URL, credential or enabled
    ///     state since the last attempt is used, makes one more attempt and saves the updated row.
    ///     If the subscription has been deleted, the delivery is dead-lettered instead of retried.
    /// </summary>
    /// <param name="attempt">The persisted delivery to resume, as read by the sweep.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public async Task ResumeAttemptAsync(
        WebhookDeliveryAttemptEntity attempt,
        CancellationToken stoppingToken
    )
    {
        WebhookSubscriptionEntity subscription = await subscriptionRepository.GetByIdAsync(
            attempt.SubscriptionId,
            stoppingToken
        );

        if (subscription is null)
        {
            attempt.Status = WebhookDeliveryAttemptStatusEnum.DeadLettered;
            attempt.LastErrorMessage = "The target subscription no longer exists.";
            attempt.NextRetryDueUtc = null;
            attempt.CompletedAtUtc = DateTimeOffset.UtcNow;

            logger.LogWarning(
                "Dead-lettering webhook delivery {AttemptId} (subscription '{SubscriptionName}'): the target subscription has been deleted.",
                attempt.Id,
                attempt.SubscriptionName
            );

            attemptRepository.Update(attempt);
            await unitOfWork.SaveChangesAsync(stoppingToken);
            return;
        }

        // A disabled subscription is deferred, neither attempted nor dead-lettered: no attempt
        // is spent on a target the operator turned off, and the delivery becomes due again one
        // sweep interval later in case the subscription is re-enabled.
        if (subscription.IsDisabled)
        {
            attempt.NextRetryDueUtc = DateTimeOffset.UtcNow.Add(options.CurrentValue.SweepInterval);
            attemptRepository.Update(attempt);
            await unitOfWork.SaveChangesAsync(stoppingToken);
            return;
        }

        DeliveryAttemptOutcome outcome = await TryDeliverOnceAsync(
            subscription,
            attempt.PayloadJson,
            stoppingToken
        );

        attempt.AttemptCount++;
        attempt.LastAttemptedAtUtc = DateTimeOffset.UtcNow;
        attempt.LastHttpStatusCode = outcome.StatusCode;
        attempt.LastErrorMessage = outcome.ErrorMessage;

        ApplyOutcome(attempt, outcome, subscription.Name);

        attemptRepository.Update(attempt);
        await unitOfWork.SaveChangesAsync(stoppingToken);
    }

    /// <summary>
    ///     This method sets the <see cref="WebhookDeliveryAttemptEntity.Status" />,
    ///     <see cref="WebhookDeliveryAttemptEntity.NextRetryDueUtc" /> and
    ///     <see cref="WebhookDeliveryAttemptEntity.CompletedAtUtc" /> of
    ///     <paramref name="attempt" /> from an attempt's outcome and logs it. Both
    ///     <see cref="DeliverToSubscriptionAsync" /> and <see cref="ResumeAttemptAsync" /> use it, so
    ///     they agree on when a delivery has succeeded or run out of attempts.
    /// </summary>
    private void ApplyOutcome(
        WebhookDeliveryAttemptEntity attempt,
        DeliveryAttemptOutcome outcome,
        string subscriptionName
    )
    {
        if (outcome.Success)
        {
            attempt.Status = WebhookDeliveryAttemptStatusEnum.Succeeded;
            attempt.NextRetryDueUtc = null;
            attempt.CompletedAtUtc = DateTimeOffset.UtcNow;

            logger.LogInformation(
                "Delivered webhook subscription '{Name}' (attempt {Attempt}/{MaxAttempts}), status {StatusCode}.",
                subscriptionName,
                attempt.AttemptCount,
                attempt.MaxAttempts,
                outcome.StatusCode
            );
            return;
        }

        if (attempt.AttemptCount < attempt.MaxAttempts)
        {
            attempt.Status = WebhookDeliveryAttemptStatusEnum.Pending;
            attempt.NextRetryDueUtc = DateTimeOffset.UtcNow.Add(
                ComputeBackoffDelay(attempt.AttemptCount)
            );

            logger.LogWarning(
                "Webhook subscription '{Name}' delivery attempt {Attempt}/{MaxAttempts} failed ({Reason}); next retry due {NextRetryDueUtc:O}.",
                subscriptionName,
                attempt.AttemptCount,
                attempt.MaxAttempts,
                outcome.ErrorMessage,
                attempt.NextRetryDueUtc
            );
            return;
        }

        attempt.Status = WebhookDeliveryAttemptStatusEnum.DeadLettered;
        attempt.NextRetryDueUtc = null;
        attempt.CompletedAtUtc = DateTimeOffset.UtcNow;

        logger.LogError(
            "Giving up on webhook subscription '{Name}' after {MaxAttempts} delivery attempts ({Reason}).",
            subscriptionName,
            attempt.MaxAttempts,
            outcome.ErrorMessage
        );
    }

    /// <summary>
    ///     This method makes one HTTP delivery attempt to the target URL of
    ///     <paramref name="subscription" />, used for both first attempts and retries. The auth
    ///     header and signature are built on every call, so a credential rotated between two
    ///     attempts is used by the next one. Transport failures, timeouts and non-2xx responses
    ///     are returned in the <see cref="DeliveryAttemptOutcome" />. Cancellation through
    ///     <paramref name="stoppingToken" /> propagates, and so does a failure to decrypt the
    ///     credentials or signing secret, which happens before the request is sent.
    /// </summary>
    private async Task<DeliveryAttemptOutcome> TryDeliverOnceAsync(
        WebhookSubscriptionEntity subscription,
        string body,
        CancellationToken stoppingToken
    )
    {
        AuthenticationHeaderValue authHeader = BuildAuthHeader(subscription);
        string signature = BuildSignature(subscription, body);

        HttpClient httpClient = httpClientFactory.CreateClient(HttpClientName);

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Post, subscription.TargetUrl)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };

            if (authHeader is not null)
                request.Headers.Authorization = authHeader;

            if (signature is not null)
                request.Headers.Add(SignatureHeaderName, signature);

            using HttpResponseMessage response = await httpClient.SendAsync(request, stoppingToken);

            if (response.IsSuccessStatusCode)
                return new DeliveryAttemptOutcome(true, (int)response.StatusCode, null);

            return new DeliveryAttemptOutcome(
                false,
                (int)response.StatusCode,
                $"received non-success status {(int)response.StatusCode}"
            );
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException
                || !stoppingToken.IsCancellationRequested
            )
        {
            return new DeliveryAttemptOutcome(false, null, exception.Message);
        }
    }

    /// <summary>
    ///     This method computes the backoff before the attempt after
    ///     <paramref name="completedAttemptNumber" />: <see cref="WebhookDeliveryOptions.InitialRetryDelay" />
    ///     doubled after each failed attempt, capped at
    ///     <see cref="WebhookDeliveryOptions.MaxRetryDelay" />.
    /// </summary>
    private TimeSpan ComputeBackoffDelay(int completedAttemptNumber)
    {
        WebhookDeliveryOptions deliveryOptions = options.CurrentValue;
        double multiplier = Math.Pow(2, completedAttemptNumber - 1);
        double candidateMilliseconds =
            deliveryOptions.InitialRetryDelay.TotalMilliseconds * multiplier;

        double cappedMilliseconds = Math.Min(
            candidateMilliseconds,
            deliveryOptions.MaxRetryDelay.TotalMilliseconds
        );

        return TimeSpan.FromMilliseconds(cappedMilliseconds);
    }

    /// <summary>
    ///     This method builds the outbound <c>Authorization</c> header value for a subscription,
    ///     decrypting its stored credentials, or returns <see langword="null" /> when its
    ///     <c>AuthMode</c> is <c>None</c> or no credentials are stored.
    /// </summary>
    private AuthenticationHeaderValue BuildAuthHeader(WebhookSubscriptionEntity subscription)
    {
        if (
            subscription.AuthMode == WebhookAuthModeEnum.None
            || subscription.AuthCredentialsEncrypted is null
        )
            return null;

        string credentialsJson = credentialProtector.UnprotectAuthCredentials(
            subscription.AuthCredentialsEncrypted
        );
        Dictionary<string, string> credentials = JsonSerializer.Deserialize<
            Dictionary<string, string>
        >(credentialsJson);

        return subscription.AuthMode switch
        {
            WebhookAuthModeEnum.Bearer => new AuthenticationHeaderValue(
                "Bearer",
                credentials.GetValueOrDefault("BearerToken")
            ),
            WebhookAuthModeEnum.Basic => new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(
                    Encoding.UTF8.GetBytes(
                        $"{credentials.GetValueOrDefault("Username")}:{credentials.GetValueOrDefault("Password")}"
                    )
                )
            ),
            _ => null,
        };
    }

    /// <summary>
    ///     This method computes the HMAC-SHA256 signature of the request body (see
    ///     <see cref="SignatureHeaderName" />), or returns <see langword="null" /> when the
    ///     subscription has no signing secret.
    /// </summary>
    private string BuildSignature(WebhookSubscriptionEntity subscription, string body)
    {
        if (subscription.HmacSigningSecretEncrypted is null)
            return null;

        string secret = credentialProtector.UnprotectHmacSigningSecret(
            subscription.HmacSigningSecretEncrypted
        );

        byte[] hash = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(body)
        );

        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    ///     This method builds an event's JSON payload:
    ///     <c>{"eventType":"...","deliveredAtUtc":"...","data":{...}}</c>, where <c>data</c> holds
    ///     the event's fields. The payload is built once and saved with the delivery, so
    ///     <c>deliveredAtUtc</c> is the time of the first attempt, also on retries. The event's
    ///     runtime <see cref="Type" /> is passed to <see cref="JsonSerializer" />: serializing
    ///     through the <see cref="IDnsServerEvent" /> interface type would write only the
    ///     interface's properties, of which there are none.
    /// </summary>
    private static string BuildPayload(IDnsServerEvent domainEvent, string eventTypeName)
    {
        JsonNode dataNode = JsonSerializer.SerializeToNode(
            domainEvent,
            domainEvent.GetType(),
            PayloadSerializerOptions
        );

        JsonObject envelope = new()
        {
            ["eventType"] = eventTypeName,
            ["deliveredAtUtc"] = DateTimeOffset.UtcNow,
            ["data"] = dataNode,
        };

        return envelope.ToJsonString(PayloadSerializerOptions);
    }

    /// <summary>
    ///     This record carries the outcome of one attempt from <see cref="TryDeliverOnceAsync" />
    ///     back to the caller that saves it.
    /// </summary>
    /// <param name="Success">Whether the endpoint returned a 2xx response.</param>
    /// <param name="StatusCode">The HTTP status code received, or <see langword="null" /> if the attempt failed before a response was ever received.</param>
    /// <param name="ErrorMessage">A short, human-readable failure description, or <see langword="null" /> on success.</param>
    private sealed record DeliveryAttemptOutcome(
        bool Success,
        int? StatusCode,
        string ErrorMessage
    );
}
