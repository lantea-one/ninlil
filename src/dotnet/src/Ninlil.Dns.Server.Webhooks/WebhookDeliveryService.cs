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
///         <see cref="ResumeDueAsync" /> when the saved <c>NextRetryDueUtc</c> is reached;
///         nothing here waits out a backoff in memory. So a pending retry, and the wait before it,
///         survive a restart. Nothing is saved before the first attempts complete (see
///         <see cref="DeliverAsync" /> for that gap).
///     </para>
///     <para>
///         <b>Database work runs in sequence, HTTP calls in parallel.</b> This class holds one
///         scoped <c>DbContext</c>, which allows one operation at a time, so every read and write
///         happens on the calling task, one after the other. Only the HTTP attempts, which touch
///         no repository, run in parallel, at most <see cref="MaxConcurrentAttempts" /> at once.
///         The outcomes of one event or one sweep are saved together with one
///         <c>SaveChangesAsync</c>.
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
    ///     This constant bounds how many HTTP attempts one event or one sweep has in flight at
    ///     once, so a large batch cannot open an unbounded number of outbound sockets.
    /// </summary>
    private const int MaxConcurrentAttempts = 16;

    /// <summary>
    ///     This field contains the JSON options every payload is serialized with: the web
    ///     defaults, so property names are camelCase like the REST API's JSON.
    /// </summary>
    private static readonly JsonSerializerOptions PayloadSerializerOptions = new(
        JsonSerializerDefaults.Web
    );

    /// <summary>
    ///     This method makes the first delivery attempt of an event to every enabled subscription
    ///     whose event-type filter matches it, then saves one
    ///     <see cref="WebhookDeliveryAttemptEntity" /> per subscription: succeeded, pending with a
    ///     retry time, or dead-lettered at once when <c>MaxDeliveryAttempts</c> is <c>1</c> or the
    ///     subscription's credentials cannot be decrypted. The attempts run in parallel, so a slow
    ///     or unreachable target does not delay the others; the rows are added and saved together
    ///     afterwards. HTTP failures are recorded in the rows, not thrown.
    ///     <para>
    ///         <b>Accepted gap</b>: if the process stops during the attempts, before the rows are
    ///         saved, the delivery is lost without a record. Events are not durable before this
    ///         point either (they live in an in-memory queue), so saving a row before the request
    ///         would only move the gap earlier, and would risk a duplicate delivery if the process
    ///         stopped between that write and the attempt. From the saved rows on, every wait and
    ///         retry survives a restart.
    ///     </para>
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

        DeliveryAttemptOutcome[] outcomes = await AttemptAllAsync(
            matchingSubscriptions,
            _ => body,
            stoppingToken
        );

        for (int index = 0; index < matchingSubscriptions.Count; index++)
        {
            WebhookSubscriptionEntity subscription = matchingSubscriptions[index];
            DeliveryAttemptOutcome outcome = outcomes[index];

            WebhookDeliveryAttemptEntity attempt = new()
            {
                Id = Guid.NewGuid(),
                CreatedAtUtc = DateTimeOffset.UtcNow,
                SubscriptionId = subscription.Id,
                SubscriptionName = subscription.Name,
                EventTypeName = eventTypeName,
                PayloadJson = body,
                AttemptCount = 1,
                MaxAttempts = Math.Max(1, subscription.MaxDeliveryAttempts),
                LastAttemptedAtUtc = DateTimeOffset.UtcNow,
                LastHttpStatusCode = outcome.StatusCode,
                LastErrorMessage = outcome.ErrorMessage,
            };

            ApplyOutcome(attempt, outcome, subscription.Name);
            await attemptRepository.AddAsync(attempt, stoppingToken);
        }

        await unitOfWork.SaveChangesAsync(stoppingToken);
    }

    /// <summary>
    ///     This method resumes one saved delivery. It is <see cref="ResumeDueAsync" /> for a single
    ///     row.
    /// </summary>
    /// <param name="attempt">The persisted delivery to resume, as read by the sweep.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public async Task ResumeAttemptAsync(
        WebhookDeliveryAttemptEntity attempt,
        CancellationToken stoppingToken
    ) => await ResumeDueAsync([attempt], stoppingToken);

    /// <summary>
    ///     This method resumes the saved deliveries that <see cref="WebhookDeliveryRetrySweepService" />
    ///     found due. It reads each distinct subscription again, one after the other, so a changed
    ///     URL, credential or enabled state since the last attempt is used. A delivery whose
    ///     subscription has been deleted is dead-lettered; one whose subscription is disabled is
    ///     deferred by one sweep interval without an attempt. The others are attempted in
    ///     parallel, their outcomes applied, and every changed row saved with one
    ///     <c>SaveChangesAsync</c>.
    ///     <para>
    ///         Accepted: the outcomes of one call are saved together, so a process stop during
    ///         the HTTP phase saves none of them. The rows stay due and are attempted again, which
    ///         delivery already allows (it is at least once).
    ///     </para>
    /// </summary>
    /// <param name="due">The persisted deliveries to resume, as read by the sweep.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>How many deliveries were attempted (not deferred or dead-lettered for a missing subscription).</returns>
    public async Task<int> ResumeDueAsync(
        IReadOnlyList<WebhookDeliveryAttemptEntity> due,
        CancellationToken stoppingToken
    )
    {
        Dictionary<Guid, WebhookSubscriptionEntity> subscriptions = [];
        foreach (Guid subscriptionId in due.Select(attempt => attempt.SubscriptionId).Distinct())
            subscriptions[subscriptionId] = await subscriptionRepository.GetByIdAsync(
                subscriptionId,
                stoppingToken
            );

        List<(
            WebhookDeliveryAttemptEntity Attempt,
            WebhookSubscriptionEntity Subscription
        )> toAttempt = [];

        foreach (WebhookDeliveryAttemptEntity attempt in due)
        {
            WebhookSubscriptionEntity subscription = subscriptions[attempt.SubscriptionId];

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
                continue;
            }

            // A disabled subscription is deferred, neither attempted nor dead-lettered: no
            // attempt is spent on a target the operator turned off, and the delivery becomes due
            // again one sweep interval later in case the subscription is re-enabled.
            if (subscription.IsDisabled)
            {
                attempt.NextRetryDueUtc = DateTimeOffset.UtcNow.Add(
                    options.CurrentValue.SweepInterval
                );
                attemptRepository.Update(attempt);
                continue;
            }

            toAttempt.Add((attempt, subscription));
        }

        DeliveryAttemptOutcome[] outcomes = await AttemptAllAsync(
            [.. toAttempt.Select(item => item.Subscription)],
            index => toAttempt[index].Attempt.PayloadJson,
            stoppingToken
        );

        for (int index = 0; index < toAttempt.Count; index++)
        {
            WebhookDeliveryAttemptEntity attempt = toAttempt[index].Attempt;
            DeliveryAttemptOutcome outcome = outcomes[index];

            attempt.AttemptCount++;
            attempt.LastAttemptedAtUtc = DateTimeOffset.UtcNow;
            attempt.LastHttpStatusCode = outcome.StatusCode;
            attempt.LastErrorMessage = outcome.ErrorMessage;

            ApplyOutcome(attempt, outcome, toAttempt[index].Subscription.Name);
            attemptRepository.Update(attempt);
        }

        await unitOfWork.SaveChangesAsync(stoppingToken);
        return toAttempt.Count;
    }

    /// <summary>
    ///     This method makes one HTTP attempt per subscription in parallel, at most
    ///     <see cref="MaxConcurrentAttempts" /> at a time, and returns the outcomes in the order
    ///     of <paramref name="subscriptions" />. It touches no repository, so it is safe to run
    ///     while the caller owns the scope's <c>DbContext</c>.
    /// </summary>
    /// <param name="subscriptions">The subscriptions to attempt.</param>
    /// <param name="body">Gives the request body for the subscription at an index.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    private async Task<DeliveryAttemptOutcome[]> AttemptAllAsync(
        IReadOnlyList<WebhookSubscriptionEntity> subscriptions,
        Func<int, string> body,
        CancellationToken stoppingToken
    )
    {
        DeliveryAttemptOutcome[] outcomes = new DeliveryAttemptOutcome[subscriptions.Count];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, subscriptions.Count),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxConcurrentAttempts,
                CancellationToken = stoppingToken,
            },
            async (index, token) =>
                outcomes[index] = await TryDeliverOnceAsync(
                    subscriptions[index],
                    body(index),
                    token
                )
        );

        return outcomes;
    }

    /// <summary>
    ///     This method sets the <see cref="WebhookDeliveryAttemptEntity.Status" />,
    ///     <see cref="WebhookDeliveryAttemptEntity.NextRetryDueUtc" /> and
    ///     <see cref="WebhookDeliveryAttemptEntity.CompletedAtUtc" /> of
    ///     <paramref name="attempt" /> from an attempt's outcome and logs it. Both
    ///     <see cref="DeliverAsync" /> and <see cref="ResumeDueAsync" /> use it, so they agree on
    ///     when a delivery has succeeded or run out of attempts. A permanent failure (see
    ///     <see cref="DeliveryAttemptOutcome.IsPermanent" />) dead-letters the delivery at once,
    ///     whatever its attempt count, because no retry can succeed.
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

        if (!outcome.IsPermanent && attempt.AttemptCount < attempt.MaxAttempts)
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

        if (outcome.IsPermanent)
        {
            logger.LogError(
                "Dead-lettering webhook subscription '{Name}' delivery at attempt {Attempt}: {Reason}",
                subscriptionName,
                attempt.AttemptCount,
                outcome.ErrorMessage
            );
            return;
        }

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
    ///     are returned in the <see cref="DeliveryAttemptOutcome" />, and so is a credential or
    ///     signing secret that cannot be decrypted (the key ring that wrote it is gone), as a
    ///     permanent failure with no request sent. Cancellation through
    ///     <paramref name="stoppingToken" /> propagates. It touches no repository, so several
    ///     calls may run at once.
    /// </summary>
    private async Task<DeliveryAttemptOutcome> TryDeliverOnceAsync(
        WebhookSubscriptionEntity subscription,
        string body,
        CancellationToken stoppingToken
    )
    {
        AuthenticationHeaderValue authHeader;
        string signature;

        try
        {
            authHeader = BuildAuthHeader(subscription);
            signature = BuildSignature(subscription, body);
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            return new DeliveryAttemptOutcome(
                false,
                null,
                "The subscription's credentials or signing secret cannot be decrypted (the key ring changed); re-enter them and recreate the subscription if it signs.",
                IsPermanent: true
            );
        }

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
    /// <param name="IsPermanent">Whether no retry can succeed, so the delivery is dead-lettered at once.</param>
    private sealed record DeliveryAttemptOutcome(
        bool Success,
        int? StatusCode,
        string ErrorMessage,
        bool IsPermanent = false
    );
}
