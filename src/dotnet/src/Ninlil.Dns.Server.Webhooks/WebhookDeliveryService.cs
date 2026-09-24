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
///     This class is this project's own outbound webhook delivery engine: given a single occurred
///     <see cref="IDnsServerEvent" />, it looks up every currently enabled
///     <see cref="WebhookSubscriptionEntity" /> whose own <c>EventTypeNames</c> includes that
///     event's registered <see cref="DnsServerEventTypeRegistry" /> name, and POSTs a JSON payload
///     to each matching subscription's own <c>TargetUrl</c> -- attaching the subscription's
///     configured bearer/basic auth header and, if an HMAC signing secret is configured, an
///     <see cref="SignatureHeaderName" /> header computed over the exact outbound request body.
///     <para>
///         <b>Only a delivery's very first attempt is ever made from here.</b> Every second or
///         later attempt (a retry, whether after a short in-process pause or after a full process
///         restart days later) is made exclusively by
///         <see cref="WebhookDeliveryRetrySweepService" />'s own periodic sweep against the
///         persisted <see cref="WebhookDeliveryAttemptEntity" /> table -- this class never itself
///         waits out a backoff delay with <see cref="Task.Delay(TimeSpan)" /> the way it once did.
///         This split is the actual fix for this feature's own previously-documented durability
///         gap: before it existed, a subscription's entire retry loop -- including every pending
///         backoff wait -- lived purely inside one detached in-memory background task, so a
///         process restart at any point mid-loop silently lost the delivery with no record it was
///         ever attempted at all. Splitting the very first attempt out from every later one keeps
///         today's low-latency "try immediately when the event happens" behavior for the common
///         case (an endpoint that is actually up), while still making every attempt beyond the
///         first fully durable: nothing about a delivery is ever persisted until its first attempt
///         has actually completed (see <see cref="DeliverToSubscriptionAsync" />'s own doc
///         comment for the one narrow, deliberately accepted gap that leaves open), but from that
///         point on, every further step -- including any wait between attempts -- is real,
///         on-disk state a fresh process can pick back up exactly where an old one left off.
///     </para>
///     <para>
///         This class is registered scoped (it depends on the scoped
///         <see cref="IWebhookSubscriptionRepository" />/<see cref="IWebhookDeliveryAttemptRepository" />)
///         and is never invoked directly from
///         <see cref="Ninlil.Dns.Server.Core.Events.DnsServerEventDispatcher" />'s own event
///         loop -- <see cref="WebhookDeliverySubscriber{TEvent}" /> is the actual
///         <c>IDnsServerEventSubscriber&lt;TEvent&gt;</c> registered for each webhook-eligible
///         event type, and always calls this class from its own detached background task, opened
///         in a fresh dependency injection scope, per architectural invariant #3.
///     </para>
/// </summary>
/// <param name="subscriptionRepository">The repository this engine reads enabled subscriptions from.</param>
/// <param name="attemptRepository">The repository this engine persists every delivery's own progress to.</param>
/// <param name="unitOfWork">The unit of work this engine commits every persisted delivery change through.</param>
/// <param name="credentialProtector">The protector this engine decrypts a subscription's own auth credentials and HMAC signing secret through.</param>
/// <param name="httpClientFactory">The factory this engine creates its outbound <see cref="HttpClient" /> from.</param>
/// <param name="options">
///     This engine's own retry/backoff timing configuration -- bound via
///     <see cref="IOptionsMonitor{TOptions}" /> and read fresh, via <c>options.CurrentValue</c>,
///     on every single delivery this (scoped) engine attempts, so a configuration write reaches
///     the very next retry/backoff computation with no restart. <see cref="WebhookDeliveryOptions.RequestTimeout" />
///     is a partial exception: it is applied to an outbound <see cref="HttpClient" /> only when
///     <see cref="IHttpClientFactory" /> actually builds a fresh handler for
///     <see cref="HttpClientName" /> (by default, roughly every two minutes, per that factory's
///     own handler-lifetime pooling) -- see <c>ServiceCollectionWebhooksExtension</c>'s own
///     <c>ConfigureHttpClient</c> callback, which reads it from the same live
///     <see cref="IOptionsMonitor{TOptions}" /> rather than a process-wide-cached
///     <see cref="IOptions{TOptions}" />, so a configuration write does eventually reach a
///     freshly built handler rather than being permanently cached at its first-ever value.
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
    ///     This constant contains the name this engine's own outbound <see cref="HttpClient" /> is
    ///     registered under via <see cref="IHttpClientFactory" />.
    /// </summary>
    public const string HttpClientName = "Ninlil.Dns.Server.Webhooks";

    /// <summary>
    ///     This constant contains the header name a subscription's own HMAC-SHA256 payload
    ///     signature (a lowercase hexadecimal digest of the exact outbound request body, keyed by
    ///     the subscription's own decrypted signing secret's UTF-8 bytes) is delivered under, so a
    ///     receiver can verify a payload really came from this server.
    /// </summary>
    public const string SignatureHeaderName = "X-Ninlil-Signature";

    /// <summary>
    ///     This property contains the JSON options every outbound payload is serialized with --
    ///     camelCase, matching every other JSON this REST API's own consumers already see, per
    ///     architectural precedent (<c>RecordsController.ToCamelCaseDataJson</c>'s own reasoning).
    /// </summary>
    private static readonly JsonSerializerOptions PayloadSerializerOptions = new(
        JsonSerializerDefaults.Web
    );

    /// <summary>
    ///     This method delivers a single occurred event to every currently enabled subscription
    ///     whose own event-type filter matches it. Each matching subscription's own delivery
    ///     runs concurrently with every other matching subscription's -- one subscription's own
    ///     target being slow or down never delays delivery to any other subscription for the same
    ///     event.
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
    ///     This method performs a single subscription's own very first delivery attempt,
    ///     synchronously, then persists the resulting <see cref="WebhookDeliveryAttemptEntity" />
    ///     for the first time -- success, a scheduled retry, or an immediate dead letter if the
    ///     subscription's own <c>MaxDeliveryAttempts</c> is <c>1</c>. This method never throws
    ///     (other than genuine cancellation) -- a single subscription's own persistent failure
    ///     must never prevent delivery to any other subscription.
    ///     <para>
    ///         <b>The one deliberately accepted non-durable window this design still has</b>: if
    ///         this process is killed during this very first attempt -- after the outbound HTTP
    ///         request has already started but before this method has finished persisting the
    ///         resulting row -- that one delivery is genuinely lost, with no record it was ever
    ///         attempted, exactly like this feature's entire retry loop used to behave before
    ///         <see cref="WebhookDeliveryAttemptEntity" /> existed at all. This is accepted rather
    ///         than engineered around: there is no event queue upstream of this call either (the
    ///         event itself was never durable to begin with -- see architectural invariant #3), so
    ///         persisting a "we're about to try" row before the network call would only move the
    ///         same narrow gap earlier without actually closing it, at the cost of a genuine
    ///         double-delivery risk if a crash instead landed between that pre-write and the
    ///         attempt's own real completion. Every attempt from this point on -- any wait between
    ///         retries, and every retry itself -- is fully durable; only this one first instant is
    ///         not.
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
    ///     This method resumes a single previously-persisted delivery that
    ///     <see cref="WebhookDeliveryRetrySweepService" /> found due for a retry -- looking the
    ///     target subscription back up fresh (never trusting a stale in-memory copy, per this
    ///     project's own documented "always re-read across a scope boundary that may span an
    ///     intervening write" lesson), performing exactly one more attempt, and persisting the
    ///     updated row. If the target subscription no longer exists at all (deleted since this
    ///     delivery was first created), this delivery is dead-lettered immediately rather than
    ///     retried forever against nothing.
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

        // A disabled subscription is deferred, not dead-lettered and not attempted: this
        // delivery's own attempt budget is not spent re-trying a target an operator has
        // deliberately turned off, but it is not silently abandoned either -- it simply becomes
        // due again after one more sweep interval, in case the subscription is re-enabled before
        // then.
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
    ///     This method applies a single attempt's own <see cref="DeliveryAttemptOutcome" /> to
    ///     <paramref name="attempt" />'s own <see cref="WebhookDeliveryAttemptEntity.Status" />/
    ///     <see cref="WebhookDeliveryAttemptEntity.NextRetryDueUtc" />/
    ///     <see cref="WebhookDeliveryAttemptEntity.CompletedAtUtc" />, and logs the outcome --
    ///     shared between a delivery's very first attempt (<see cref="DeliverToSubscriptionAsync" />)
    ///     and every attempt made from a resumed sweep (<see cref="ResumeAttemptAsync" />) so the
    ///     two paths can never drift out of sync over what "succeeded"/"exhausted" actually means.
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
    ///     This method performs exactly one outbound HTTP delivery attempt against
    ///     <paramref name="subscription" />'s own target URL, building its own auth header and
    ///     HMAC signature fresh on every call (so a credential rotated between two attempts of the
    ///     same delivery is honored by the very next attempt) -- shared by both
    ///     <see cref="DeliverToSubscriptionAsync" />'s own first attempt and
    ///     <see cref="ResumeAttemptAsync" />'s own later ones. This method never throws (other
    ///     than genuine cancellation); every failure, transport-level or a non-success response
    ///     alike, is captured in the returned <see cref="DeliveryAttemptOutcome" /> instead.
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
    ///     This method computes the exponential backoff delay before the attempt immediately
    ///     following <paramref name="completedAttemptNumber" /> -- doubling from
    ///     <see cref="WebhookDeliveryOptions.InitialRetryDelay" /> after each failed attempt,
    ///     capped at <see cref="WebhookDeliveryOptions.MaxRetryDelay" />, the exact same shape
    ///     <c>RabbitMqDnsServerEventBrokerTransport</c>'s own reconnect backoff uses.
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
    ///     decrypting its own stored auth credentials, or returns <see langword="null" /> when the
    ///     subscription's own <c>AuthMode</c> is <c>None</c>.
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
    ///     This method computes a subscription's own HMAC-SHA256 signature over the exact outbound
    ///     request body, keyed by the subscription's own decrypted signing secret's UTF-8 bytes,
    ///     or returns <see langword="null" /> when the subscription has no signing secret
    ///     configured.
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
    ///     This method builds a single event's own outbound JSON payload:
    ///     <c>{"eventType":"...","deliveredAtUtc":"...","data":{...}}</c>, where <c>data</c> is the
    ///     event's own fields. <paramref name="domainEvent" />'s own concrete runtime
    ///     <see cref="Type" /> is passed to <see cref="JsonSerializer" /> explicitly, rather than
    ///     relying on the compile-time <see cref="IDnsServerEvent" /> parameter type -- the exact
    ///     fix for the "single-argument <c>Serialize&lt;TSource&gt;</c> overload" trap: passing an interface- or
    ///     marker-typed value to the generic overload silently serializes based on that interface's
    ///     own (here, zero) properties instead of the real record's fields.
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
    ///     This record carries a single delivery attempt's own outcome back from
    ///     <see cref="TryDeliverOnceAsync" /> to whichever caller (the first, live attempt or a
    ///     resumed retry) needs to persist it.
    /// </summary>
    /// <param name="Success">Whether the subscriber's own endpoint returned a 2xx response.</param>
    /// <param name="StatusCode">The HTTP status code received, or <see langword="null" /> if the attempt failed before a response was ever received.</param>
    /// <param name="ErrorMessage">A short, human-readable failure description, or <see langword="null" /> on success.</param>
    private sealed record DeliveryAttemptOutcome(
        bool Success,
        int? StatusCode,
        string ErrorMessage
    );
}
