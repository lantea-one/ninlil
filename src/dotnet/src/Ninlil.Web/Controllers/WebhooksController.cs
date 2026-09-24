using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Core.Events.Broker;
using Ninlil.Dns.Server.Webhooks;
using Ninlil.Web.Examples.Webhooks;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller administers this server's own outbound subscribable webhooks -- a
///     "think SendGrid's Event Webhook" feature. It owns the schema, the event catalog, and this
///     REST management surface; a subscription created, updated, or deleted here is picked up
///     directly by <c>Server.Webhooks</c>'s own delivery engine, which subscribes to the curated
///     "noteworthy" event set and POSTs to each matching subscription's own <c>TargetUrl</c>.
///     Every action requires the <c>webhooks:read</c> or <c>webhooks:write</c> permission.
/// </summary>
/// <param name="subscriptionRepository">The repository this controller reads and writes subscriptions through.</param>
/// <param name="unitOfWork">The unit of work every staged change is committed through.</param>
/// <param name="credentialProtector">
///     The protector this controller encrypts a subscription's own auth credential(s) and HMAC
///     signing secret through before either is ever persisted.
/// </param>
/// <param name="attemptRepository">
///     The repository this controller reads a subscription's own persisted delivery-attempt
///     history through -- written exclusively by <c>Server.Webhooks</c>'s own delivery engine,
///     never by this controller (see <see cref="ListDeliveryAttempts" />'s own doc comment).
/// </param>
[ApiController]
[Route("api/webhooks")]
public class WebhooksController(
    IWebhookSubscriptionRepository subscriptionRepository,
    IUnitOfWork unitOfWork,
    WebhookCredentialProtector credentialProtector,
    IWebhookDeliveryAttemptRepository attemptRepository
) : ControllerBase
{
    /// <summary>
    ///     This constant contains the length, in bytes, of a server-generated HMAC signing
    ///     secret -- matching HMAC-SHA256's own 256-bit block size, the same length
    ///     <c>TsigKeysController</c>'s own server-generated shared secret already uses.
    /// </summary>
    private const int GeneratedHmacSigningSecretLengthBytes = 32;

    /// <summary>
    ///     This action lists every outbound webhook subscription this server knows about.
    /// </summary>
    [HttpGet]
    [RequirePermission("webhooks:read")]
    [SwaggerResponseExample(
        StatusCodes.Status200OK,
        typeof(WebhookSubscriptionDetailPagedResultExample)
    )]
    public async Task<ActionResult<PagedResult<WebhookSubscriptionDetail>>> List(
        [FromQuery] PaginationQuery pagination,
        CancellationToken stoppingToken
    )
    {
        (int skip, int take) = (pagination ?? new PaginationQuery()).Normalize();

        // Both the page itself and its total count are pushed down to the storage engine -- a
        // real, translated ORDER BY/OFFSET/LIMIT (or the MongoDB equivalent) -- rather than
        // materializing every subscription into memory first. This is a search of the
        // subscription set itself, distinct from ListDeliveryAttempts's own separate,
        // per-subscription delivery-history listing further down this controller.
        IReadOnlyList<WebhookSubscriptionEntity> subscriptions =
            await subscriptionRepository.SearchAsync(skip, take, stoppingToken);
        int totalCount = await subscriptionRepository.CountAsync(stoppingToken);

        return Ok(
            new PagedResult<WebhookSubscriptionDetail>(
                [.. subscriptions.Select(ToDetail)],
                skip,
                take,
                totalCount
            )
        );
    }

    /// <summary>
    ///     This action looks up a single outbound webhook subscription by its identifier.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("webhooks:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(WebhookSubscriptionDetailExample))]
    public async Task<ActionResult<WebhookSubscriptionDetail>> Get(
        Guid id,
        CancellationToken stoppingToken
    )
    {
        WebhookSubscriptionEntity subscription = await subscriptionRepository.GetByIdAsync(
            id,
            stoppingToken
        );
        if (subscription is null)
            throw new NotFoundException("WebhookSubscription", id.ToString());

        return Ok(ToDetail(subscription));
    }

    /// <summary>
    ///     This action creates a new outbound webhook subscription. The plaintext auth
    ///     credential(s) and (if enabled) HMAC signing secret are returned exactly once, in this
    ///     action's own response -- every later request for this subscription omits both.
    /// </summary>
    [HttpPost]
    [RequirePermission("webhooks:write")]
    [SwaggerRequestExample(
        typeof(WebhookSubscriptionCreateRequest),
        typeof(WebhookSubscriptionCreateRequestExample)
    )]
    [SwaggerResponseExample(
        StatusCodes.Status201Created,
        typeof(WebhookSubscriptionCreateResponseExample)
    )]
    public async Task<ActionResult<WebhookSubscriptionCreateResponse>> Create(
        WebhookSubscriptionCreateRequest request,
        CancellationToken stoppingToken
    )
    {
        if (string.IsNullOrWhiteSpace(request?.Name))
            throw new ValidationException("'name' is required.");

        if (await subscriptionRepository.FindByNameAsync(request.Name, stoppingToken) is not null)
            throw new ConflictException(
                $"A webhook subscription named '{request.Name}' already exists."
            );

        if (!TryValidateTargetUrl(request.TargetUrl, out string targetUrlError))
            throw new ValidationException(targetUrlError);

        if (!TryValidateEventTypeNames(request.EventTypeNames, out string eventTypeError))
            throw new ValidationException(eventTypeError);

        if (
            !TryValidateAuthCredentials(
                request.AuthMode,
                request.AuthCredentials,
                out string authError
            )
        )
            throw new ValidationException(authError);

        if (request.MaxDeliveryAttempts <= 0)
            throw new ValidationException("'maxDeliveryAttempts' must be greater than zero.");

        WebhookSubscriptionEntity subscription = new()
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            TargetUrl = request.TargetUrl,
            EventTypeNames = [.. request.EventTypeNames.Distinct(StringComparer.Ordinal)],
            AuthMode = request.AuthMode,
            AuthCredentialsEncrypted =
                request.AuthMode == WebhookAuthModeEnum.None
                    ? null
                    : credentialProtector.ProtectAuthCredentials(
                        JsonSerializer.Serialize(request.AuthCredentials)
                    ),
            MaxDeliveryAttempts = request.MaxDeliveryAttempts,
            IsDisabled = request.IsDisabled,
            Comments = request.Comments,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        string plaintextHmacSigningSecret = null;
        if (request.EnableHmacSigning)
        {
            plaintextHmacSigningSecret = string.IsNullOrWhiteSpace(request.HmacSigningSecret)
                ? Convert.ToBase64String(
                    RandomNumberGenerator.GetBytes(GeneratedHmacSigningSecretLengthBytes)
                )
                : request.HmacSigningSecret;

            subscription.HmacSigningSecretEncrypted = credentialProtector.ProtectHmacSigningSecret(
                plaintextHmacSigningSecret
            );
        }

        await subscriptionRepository.AddAsync(subscription, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return CreatedAtAction(
            nameof(Get),
            new { id = subscription.Id },
            new WebhookSubscriptionCreateResponse(
                ToDetail(subscription),
                request.AuthMode == WebhookAuthModeEnum.None ? null : request.AuthCredentials,
                plaintextHmacSigningSecret
            )
        );
    }

    /// <summary>
    ///     This action fully replaces an existing webhook subscription's metadata and auth
    ///     credentials. The subscription's own HMAC signing secret is left untouched -- see
    ///     <see cref="WebhookSubscriptionWriteRequest" /> for why.
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("webhooks:write")]
    [SwaggerRequestExample(
        typeof(WebhookSubscriptionWriteRequest),
        typeof(WebhookSubscriptionWriteRequestExample)
    )]
    public async Task<IActionResult> Replace(
        Guid id,
        WebhookSubscriptionWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        WebhookSubscriptionEntity subscription = await subscriptionRepository.GetByIdAsync(
            id,
            stoppingToken
        );
        if (subscription is null)
            throw new NotFoundException("WebhookSubscription", id.ToString());

        if (string.IsNullOrWhiteSpace(request?.Name))
            throw new ValidationException("'name' is required.");

        WebhookSubscriptionEntity existingByName = await subscriptionRepository.FindByNameAsync(
            request.Name,
            stoppingToken
        );
        if (existingByName is not null && existingByName.Id != id)
            throw new ConflictException(
                $"A webhook subscription named '{request.Name}' already exists."
            );

        if (!TryValidateTargetUrl(request.TargetUrl, out string targetUrlError))
            throw new ValidationException(targetUrlError);

        if (!TryValidateEventTypeNames(request.EventTypeNames, out string eventTypeError))
            throw new ValidationException(eventTypeError);

        if (
            !TryValidateAuthCredentials(
                request.AuthMode,
                request.AuthCredentials,
                out string authError
            )
        )
            throw new ValidationException(authError);

        if (request.MaxDeliveryAttempts <= 0)
            throw new ValidationException("'maxDeliveryAttempts' must be greater than zero.");

        subscription.Name = request.Name;
        subscription.TargetUrl = request.TargetUrl;
        subscription.EventTypeNames = [.. request.EventTypeNames.Distinct(StringComparer.Ordinal)];
        subscription.AuthMode = request.AuthMode;
        subscription.AuthCredentialsEncrypted =
            request.AuthMode == WebhookAuthModeEnum.None
                ? null
                : credentialProtector.ProtectAuthCredentials(
                    JsonSerializer.Serialize(request.AuthCredentials)
                );
        subscription.MaxDeliveryAttempts = request.MaxDeliveryAttempts;
        subscription.IsDisabled = request.IsDisabled;
        subscription.Comments = request.Comments;
        subscription.UpdatedAtUtc = DateTimeOffset.UtcNow;

        subscriptionRepository.Update(subscription);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This action partially updates an existing webhook subscription via RFC 7396 JSON
    ///     Merge Patch: a field absent from the patch document is left untouched, a field present
    ///     with a JSON <c>null</c> clears it (where the underlying field is itself nullable), and
    ///     any other present field overwrites it. As with <see cref="Replace" />, this
    ///     subscription's own HMAC signing secret cannot be changed this way.
    /// </summary>
    [HttpPatch("{id:guid}")]
    [RequirePermission("webhooks:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(WebhookSubscriptionPatchRequestExample))]
    public async Task<IActionResult> Patch(
        Guid id,
        [FromBody] JsonElement patch,
        CancellationToken stoppingToken
    )
    {
        WebhookSubscriptionEntity subscription = await subscriptionRepository.GetByIdAsync(
            id,
            stoppingToken
        );
        if (subscription is null)
            throw new NotFoundException("WebhookSubscription", id.ToString());

        if (patch.TryGetMergePatchField("name", out JsonElement nameElement))
        {
            string newName = nameElement.GetString();
            if (string.IsNullOrWhiteSpace(newName))
                throw new ValidationException("'name' must not be empty.");

            WebhookSubscriptionEntity existingByName = await subscriptionRepository.FindByNameAsync(
                newName,
                stoppingToken
            );
            if (existingByName is not null && existingByName.Id != id)
                throw new ConflictException(
                    $"A webhook subscription named '{newName}' already exists."
                );

            subscription.Name = newName;
        }

        if (patch.TryGetMergePatchField("targetUrl", out JsonElement targetUrlElement))
        {
            string newTargetUrl = targetUrlElement.GetString();
            if (!TryValidateTargetUrl(newTargetUrl, out string targetUrlError))
                throw new ValidationException(targetUrlError);

            subscription.TargetUrl = newTargetUrl;
        }

        if (patch.TryGetMergePatchField("eventTypeNames", out JsonElement eventTypeNamesElement))
        {
            List<string> newEventTypeNames = JsonSerializer.Deserialize<List<string>>(
                eventTypeNamesElement.GetRawText()
            );

            if (!TryValidateEventTypeNames(newEventTypeNames, out string eventTypeError))
                throw new ValidationException(eventTypeError);

            subscription.EventTypeNames = [.. newEventTypeNames.Distinct(StringComparer.Ordinal)];
        }

        WebhookAuthModeEnum authMode = subscription.AuthMode;
        if (patch.TryGetMergePatchField("authMode", out JsonElement authModeElement))
            authMode = JsonSerializer.Deserialize<WebhookAuthModeEnum>(
                authModeElement.GetRawText()
            );

        if (patch.TryGetMergePatchField("authCredentials", out JsonElement authCredentialsElement))
        {
            Dictionary<string, string> newAuthCredentials =
                authCredentialsElement.ValueKind == JsonValueKind.Null
                    ? null
                    : JsonSerializer.Deserialize<Dictionary<string, string>>(
                        authCredentialsElement.GetRawText()
                    );

            if (!TryValidateAuthCredentials(authMode, newAuthCredentials, out string authError))
                throw new ValidationException(authError);

            subscription.AuthMode = authMode;
            subscription.AuthCredentialsEncrypted =
                authMode == WebhookAuthModeEnum.None
                    ? null
                    : credentialProtector.ProtectAuthCredentials(
                        JsonSerializer.Serialize(newAuthCredentials)
                    );
        }
        else if (authMode != subscription.AuthMode)
        {
            // The auth mode changed without new credentials to match it -- the existing,
            // still-encrypted credentials almost certainly do not have the right shape for the
            // new mode, so this is rejected rather than silently accepted, mirroring
            // CloudProviderCredentialsController's own identical guard for 'providerType'.
            throw new ValidationException(
                "'authMode' cannot be changed without also supplying a matching 'authCredentials' object."
            );
        }

        if (
            patch.TryGetMergePatchField(
                "maxDeliveryAttempts",
                out JsonElement maxDeliveryAttemptsElement
            )
        )
        {
            int newMaxDeliveryAttempts = maxDeliveryAttemptsElement.GetInt32();
            if (newMaxDeliveryAttempts <= 0)
                throw new ValidationException("'maxDeliveryAttempts' must be greater than zero.");

            subscription.MaxDeliveryAttempts = newMaxDeliveryAttempts;
        }

        if (patch.TryGetMergePatchField("isDisabled", out JsonElement isDisabled))
            subscription.IsDisabled = isDisabled.GetBoolean();

        if (patch.TryGetMergePatchField("comments", out JsonElement comments))
            subscription.Comments =
                comments.ValueKind == JsonValueKind.Null ? null : comments.GetString();

        subscription.UpdatedAtUtc = DateTimeOffset.UtcNow;
        subscriptionRepository.Update(subscription);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This action lists a single webhook subscription's own persisted delivery-attempt
    ///     history, most-recently-created first -- every real delivery
    ///     <c>WebhookDeliveryService</c>/<c>WebhookDeliveryRetrySweepService</c> has ever recorded
    ///     for it, from its very first attempt through to whichever terminal outcome (or ongoing
    ///     retry) it currently stands at. This is a read-only view: there is no action anywhere on
    ///     this controller to create, edit, or manually trigger a retry of a delivery attempt --
    ///     a row here is written exclusively by the delivery engine itself, on the server's own
    ///     schedule, never by an operator.
    /// </summary>
    [HttpGet("{id:guid}/delivery-attempts")]
    [RequirePermission("webhooks:read")]
    [SwaggerResponseExample(
        StatusCodes.Status200OK,
        typeof(WebhookDeliveryAttemptDetailPagedResultExample)
    )]
    public async Task<ActionResult<PagedResult<WebhookDeliveryAttemptDetail>>> ListDeliveryAttempts(
        Guid id,
        [FromQuery] PaginationQuery pagination,
        CancellationToken stoppingToken
    )
    {
        WebhookSubscriptionEntity subscription = await subscriptionRepository.GetByIdAsync(
            id,
            stoppingToken
        );
        if (subscription is null)
            throw new NotFoundException("WebhookSubscription", id.ToString());

        (int skip, int take) = (pagination ?? new PaginationQuery()).Normalize();

        // Pushed down as a real, indexed WHERE-filtered query on both supported storage engines
        // (see WebhookDeliveryAttemptRepository/WebhookDeliveryAttemptMongoRepository, the latter
        // carrying a dedicated SubscriptionId index for exactly this lookup shape) -- never a
        // full-collection scan materialized into memory first. The final skip/take page slice
        // itself is still done in memory; pushing the pagination down into the repository query
        // as well, as every other paged list endpoint already does, is a known, deferred
        // improvement.
        IReadOnlyList<WebhookDeliveryAttemptEntity> attempts = await attemptRepository.ListAsync(
            attempt => attempt.SubscriptionId == id,
            stoppingToken
        );

        List<WebhookDeliveryAttemptEntity> ordered =
        [
            .. attempts.OrderByDescending(attempt => attempt.CreatedAtUtc),
        ];

        return Ok(
            new PagedResult<WebhookDeliveryAttemptDetail>(
                [.. ordered.Skip(skip).Take(take).Select(ToDeliveryAttemptDetail)],
                skip,
                take,
                ordered.Count
            )
        );
    }

    /// <summary>
    ///     This action permanently deletes an outbound webhook subscription.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("webhooks:write")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken stoppingToken)
    {
        WebhookSubscriptionEntity subscription = await subscriptionRepository.GetByIdAsync(
            id,
            stoppingToken
        );
        if (subscription is null)
            throw new NotFoundException("WebhookSubscription", id.ToString());

        subscriptionRepository.Remove(subscription);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This method validates a caller-supplied target URL: it must parse as an absolute
    ///     <c>http</c>/<c>https</c> URL -- anything else could never actually receive an
    ///     outbound POST from the delivery engine.
    /// </summary>
    private static bool TryValidateTargetUrl(string targetUrl, out string error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            error = "'targetUrl' is required.";
            return false;
        }

        if (
            !Uri.TryCreate(targetUrl, UriKind.Absolute, out Uri parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
        )
        {
            error = $"'{targetUrl}' is not a valid absolute http(s) URL.";
            return false;
        }

        return true;
    }

    /// <summary>
    ///     This method validates a caller-supplied event type name list against the real,
    ///     registered <see cref="DnsServerEventTypeRegistry" /> -- rejecting an unknown name with
    ///     an immediate, specific <c>400</c> rather than persisting a subscription the delivery
    ///     engine could never actually match against anything, the same
    ///     "validate against the real registry/parser" discipline
    ///     <see cref="ForwardersController" />'s own <c>definitionString</c> check already
    ///     applies.
    /// </summary>
    private static bool TryValidateEventTypeNames(List<string> eventTypeNames, out string error)
    {
        error = null;

        if (eventTypeNames is null || eventTypeNames.Count == 0)
        {
            error = "'eventTypeNames' must contain at least one event type name.";
            return false;
        }

        foreach (string eventTypeName in eventTypeNames)
        {
            try
            {
                DnsServerEventTypeRegistry.GetType(eventTypeName);
            }
            catch (InvalidOperationException)
            {
                error = $"'{eventTypeName}' is not a registered event type.";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     This method validates that a subscription's own auth credentials match what its
    ///     declared auth mode actually requires, mirroring
    ///     <c>CloudProviderCredentialsController.TryValidateCredentials</c>'s own shape for a
    ///     different secret-bearing entity type.
    /// </summary>
    private static bool TryValidateAuthCredentials(
        WebhookAuthModeEnum authMode,
        Dictionary<string, string> credentials,
        out string error
    )
    {
        error = null;

        switch (authMode)
        {
            case WebhookAuthModeEnum.None:
                return true;

            case WebhookAuthModeEnum.Bearer:
                if (
                    credentials is null
                    || !credentials.TryGetValue("BearerToken", out string bearerToken)
                    || string.IsNullOrWhiteSpace(bearerToken)
                )
                {
                    error =
                        "'authCredentials' must include a non-empty 'BearerToken' field when 'authMode' is 'Bearer'.";
                    return false;
                }
                return true;

            case WebhookAuthModeEnum.Basic:
                if (
                    credentials is null
                    || !credentials.TryGetValue("Username", out string username)
                    || string.IsNullOrWhiteSpace(username)
                    || !credentials.TryGetValue("Password", out string password)
                    || string.IsNullOrWhiteSpace(password)
                )
                {
                    error =
                        "'authCredentials' must include non-empty 'Username' and 'Password' fields when 'authMode' is 'Basic'.";
                    return false;
                }
                return true;

            default:
                error = $"'{authMode}' is not a supported webhook auth mode.";
                return false;
        }
    }

    /// <summary>
    ///     This method converts a webhook subscription entity into its REST representation,
    ///     deliberately never including its own auth credentials or HMAC signing secret.
    /// </summary>
    private static WebhookSubscriptionDetail ToDetail(WebhookSubscriptionEntity subscription) =>
        new(
            subscription.Id,
            subscription.Name,
            subscription.TargetUrl,
            subscription.EventTypeNames,
            subscription.AuthMode,
            subscription.HmacSigningSecretEncrypted is not null,
            subscription.MaxDeliveryAttempts,
            subscription.IsDisabled,
            subscription.Comments,
            subscription.CreatedAtUtc,
            subscription.UpdatedAtUtc
        );

    /// <summary>
    ///     This method converts a persisted delivery-attempt entity into its REST representation.
    /// </summary>
    private static WebhookDeliveryAttemptDetail ToDeliveryAttemptDetail(
        WebhookDeliveryAttemptEntity attempt
    ) =>
        new(
            attempt.Id,
            attempt.SubscriptionId,
            attempt.SubscriptionName,
            attempt.EventTypeName,
            attempt.PayloadJson,
            attempt.Status,
            attempt.AttemptCount,
            attempt.MaxAttempts,
            attempt.LastAttemptedAtUtc,
            attempt.NextRetryDueUtc,
            attempt.LastHttpStatusCode,
            attempt.LastErrorMessage,
            attempt.CompletedAtUtc,
            attempt.CreatedAtUtc
        );
}
