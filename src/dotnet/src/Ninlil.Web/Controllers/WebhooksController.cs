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
using Ninlil.Web.Routing;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller manages outbound webhook subscriptions: an operator names an HTTP(S)
///     endpoint and a set of event types, and the delivery engine in <c>Server.Webhooks</c> POSTs
///     each matching event there, with optional Bearer or Basic authentication and an optional
///     HMAC signature. Changes made here are picked up by that engine directly. Reads need
///     <c>webhooks:read</c> and changes need <c>webhooks:write</c>.
/// </summary>
/// <param name="subscriptionRepository">Reads and writes subscriptions.</param>
/// <param name="unitOfWork">Commits staged changes.</param>
/// <param name="credentialProtector">
///     Encrypts a subscription's authentication credentials and HMAC signing secret before they
///     are stored.
/// </param>
/// <param name="attemptRepository">
///     Reads a subscription's delivery-attempt history, which only the delivery engine writes
///     (see <see cref="ListDeliveryAttempts" />).
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
    ///     This constant contains the length, in bytes, of a generated HMAC signing secret: 256
    ///     bits, the HMAC-SHA256 output size, and the same length <c>TsigKeysController</c>
    ///     generates.
    /// </summary>
    private const int GeneratedHmacSigningSecretLengthBytes = 32;

    /// <summary>
    ///     This action lists webhook subscriptions, one page at a time, without their credentials
    ///     or signing secrets. Requires <c>webhooks:read</c>.
    /// </summary>
    [HttpGet]
    [ReadFromReplica]
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

        // The page and the total count are both computed by the storage engine (ORDER
        // BY/OFFSET/LIMIT or the MongoDB equivalent), not by loading every subscription into
        // memory.
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
    ///     This action returns one webhook subscription, without its credentials or signing
    ///     secret. Returns 404 for an unknown id. Requires <c>webhooks:read</c>.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ReadFromReplica]
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
    ///     This action creates a webhook subscription. When HMAC signing is enabled and no secret
    ///     is supplied, a random 32-byte secret is generated. The plaintext credentials and signing
    ///     secret are returned in this response only; later reads never include them. Returns 400
    ///     for a missing name, a target that is not an absolute http(s) URL, an unknown event type,
    ///     credentials that do not fit the auth mode, or a non-positive attempt limit, and 409 when
    ///     the name is taken. Requires <c>webhooks:write</c>.
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
    ///     This action replaces a webhook subscription's settings and authentication credentials.
    ///     The HMAC signing secret is left unchanged (see
    ///     <see cref="WebhookSubscriptionWriteRequest" />). Validation and errors are as for
    ///     <see cref="Create" />, plus 404 for an unknown id. Requires <c>webhooks:write</c>.
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
    ///     This action updates a webhook subscription as an RFC 7396 merge patch: an absent field
    ///     is unchanged, a JSON <c>null</c> clears a nullable field, and any other value replaces
    ///     it. Changing <c>authMode</c> requires a matching <c>authCredentials</c> in the same
    ///     patch. The HMAC signing secret cannot be changed. Requires <c>webhooks:write</c>.
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
            // The stored credentials almost certainly do not fit the new mode, so a mode change
            // without new credentials is rejected, as CloudProviderCredentialsController does for
            // 'providerType'.
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
    ///     This action lists one subscription's delivery attempts, newest first, one page at a
    ///     time: every delivery <c>WebhookDeliveryService</c> and
    ///     <c>WebhookDeliveryRetrySweepService</c> have recorded for it, from the first try to its
    ///     final outcome or pending retry. It is read only; the API has no way to create, edit or
    ///     retry an attempt. Returns 404 for an unknown subscription. Requires
    ///     <c>webhooks:read</c>.
    /// </summary>
    [HttpGet("{id:guid}/delivery-attempts")]
    [ReadFromReplica]
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

        IReadOnlyList<WebhookDeliveryAttemptEntity> attempts =
            await attemptRepository.SearchBySubscriptionIdAsync(id, skip, take, stoppingToken);
        int total = await attemptRepository.CountBySubscriptionIdAsync(id, stoppingToken);

        return Ok(
            new PagedResult<WebhookDeliveryAttemptDetail>(
                [.. attempts.Select(ToDeliveryAttemptDetail)],
                skip,
                take,
                total
            )
        );
    }

    /// <summary>
    ///     This action deletes a webhook subscription. Returns 404 for an unknown id. Requires
    ///     <c>webhooks:write</c>.
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
    ///     This method checks that a target URL is an absolute <c>http</c> or <c>https</c> URL, the
    ///     only kind the delivery engine can POST to.
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
    ///     This method checks every event type name against
    ///     <see cref="DnsServerEventTypeRegistry" />, so an unknown name returns 400 at once
    ///     instead of saving a subscription that could never match an event.
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
    ///     This method checks that the credentials fit the auth mode: nothing for <c>None</c>, a
    ///     <c>BearerToken</c> for <c>Bearer</c>, a <c>Username</c> and <c>Password</c> for
    ///     <c>Basic</c>. <c>CloudCredentialValidator</c> does the same for cloud provider
    ///     credentials.
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
    ///     This method converts a subscription entity to its API representation, which never
    ///     includes the credentials or the signing secret, only whether a secret is set.
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
    ///     This method converts a delivery-attempt entity to its API representation.
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
