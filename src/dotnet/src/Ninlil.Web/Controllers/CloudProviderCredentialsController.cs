using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Authoritative.CloudSync;
using Ninlil.Web.Examples.CloudProviderCredentials;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller administers this server's own cloud DNS provider credential sets --
///     the Cloudflare/Route 53/DigitalOcean API credentials a <c>CloudPrimary</c>/
///     <c>CloudSecondary</c> zone's own <c>CloudSync/</c> connector authenticates through (see
///     <c>CloudDnsZoneSyncService</c>). A credential set is not scoped to any one zone -- multiple
///     zones may share a single operator account's own credentials -- so, exactly like
///     <see cref="TsigKeysController" />'s own relationship to <c>ZoneEntity.TsigKeyName</c>, a
///     zone references one by identifier, through its own <c>CloudProviderCredentialId</c> field
///     (see <see cref="ZonesController" />), rather than through containment. Every action
///     requires the <c>cloudprovidercredentials:read</c> or <c>cloudprovidercredentials:write</c>
///     permission.
/// </summary>
/// <param name="credentialRepository">The repository this controller reads and writes credential sets through.</param>
/// <param name="unitOfWork">The unit of work every staged change is committed through.</param>
/// <param name="protector">
///     The protector this controller encrypts a new or rotated credential set's own secret
///     material through before it is ever persisted, and decrypts it back through when a caller
///     rotates only part of it via <c>PATCH</c> -- the same protector <c>CloudDnsZoneSyncService</c>
///     itself decrypts a credential set through at sync time.
/// </param>
[ApiController]
[Route("api/cloud-provider-credentials")]
public class CloudProviderCredentialsController(
    ICloudDnsProviderCredentialRepository credentialRepository,
    IUnitOfWork unitOfWork,
    CloudCredentialProtector protector
) : ControllerBase
{
    /// <summary>
    ///     This action lists every cloud DNS provider credential set this server knows about.
    /// </summary>
    [HttpGet]
    [RequirePermission("cloudprovidercredentials:read")]
    [SwaggerResponseExample(
        StatusCodes.Status200OK,
        typeof(CloudProviderCredentialDetailPagedResultExample)
    )]
    public async Task<ActionResult<PagedResult<CloudProviderCredentialDetail>>> List(
        [FromQuery] PaginationQuery pagination,
        CancellationToken stoppingToken
    )
    {
        (int skip, int take) = (pagination ?? new PaginationQuery()).Normalize();

        // Both the page itself and its total count are pushed down to the storage engine -- a
        // real, translated ORDER BY/OFFSET/LIMIT (or the MongoDB equivalent) -- rather than
        // materializing every credential set into memory first.
        IReadOnlyList<CloudDnsProviderCredentialEntity> credentials =
            await credentialRepository.SearchAsync(skip, take, stoppingToken);
        int totalCount = await credentialRepository.CountAsync(stoppingToken);

        return Ok(
            new PagedResult<CloudProviderCredentialDetail>(
                [.. credentials.Select(ToDetail)],
                skip,
                take,
                totalCount
            )
        );
    }

    /// <summary>
    ///     This action looks up a single cloud DNS provider credential set by its identifier.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("cloudprovidercredentials:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(CloudProviderCredentialDetailExample))]
    public async Task<ActionResult<CloudProviderCredentialDetail>> Get(
        Guid id,
        CancellationToken stoppingToken
    )
    {
        CloudDnsProviderCredentialEntity credential = await credentialRepository.GetByIdAsync(
            id,
            stoppingToken
        );
        if (credential is null)
            throw new NotFoundException("CloudProviderCredential", id.ToString());

        return Ok(ToDetail(credential));
    }

    /// <summary>
    ///     This action creates a new cloud DNS provider credential set. The plaintext secret
    ///     material supplied here is returned exactly once, in this action's own response --
    ///     every later request for this credential set omits it entirely.
    /// </summary>
    [HttpPost]
    [RequirePermission("cloudprovidercredentials:write")]
    [SwaggerRequestExample(
        typeof(CloudProviderCredentialCreateRequest),
        typeof(CloudProviderCredentialCreateRequestExample)
    )]
    [SwaggerResponseExample(
        StatusCodes.Status201Created,
        typeof(CloudProviderCredentialCreateResponseExample)
    )]
    public async Task<ActionResult<CloudProviderCredentialCreateResponse>> Create(
        CloudProviderCredentialCreateRequest request,
        CancellationToken stoppingToken
    )
    {
        if (string.IsNullOrWhiteSpace(request?.Name))
            throw new ValidationException("'name' is required.");

        if (await credentialRepository.FindByNameAsync(request.Name, stoppingToken) is not null)
            throw new ConflictException(
                $"A cloud provider credential set named '{request.Name}' already exists."
            );

        CloudCredentialValidator.ValidateOrThrow(request.ProviderType, request.Credentials);

        CloudDnsProviderCredentialEntity credential = new()
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            ProviderType = request.ProviderType,
            CredentialsEncrypted = protector.Protect(request.Credentials),
            IsDisabled = request.IsDisabled,
            Comments = request.Comments,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        await credentialRepository.AddAsync(credential, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return CreatedAtAction(
            nameof(Get),
            new { id = credential.Id },
            new CloudProviderCredentialCreateResponse(ToDetail(credential), request.Credentials)
        );
    }

    /// <summary>
    ///     This action fully replaces an existing cloud DNS provider credential set, including
    ///     its own secret material -- see <see cref="CloudProviderCredentialWriteRequest" /> for
    ///     why this is deliberately different from how <see cref="TsigKeysController.Replace" />
    ///     handles a TSIG key's own shared secret.
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("cloudprovidercredentials:write")]
    [SwaggerRequestExample(
        typeof(CloudProviderCredentialWriteRequest),
        typeof(CloudProviderCredentialWriteRequestExample)
    )]
    public async Task<IActionResult> Replace(
        Guid id,
        CloudProviderCredentialWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        CloudDnsProviderCredentialEntity credential = await credentialRepository.GetByIdAsync(
            id,
            stoppingToken
        );
        if (credential is null)
            throw new NotFoundException("CloudProviderCredential", id.ToString());

        if (string.IsNullOrWhiteSpace(request?.Name))
            throw new ValidationException("'name' is required.");

        CloudDnsProviderCredentialEntity existingByName =
            await credentialRepository.FindByNameAsync(request.Name, stoppingToken);
        if (existingByName is not null && existingByName.Id != id)
            throw new ConflictException(
                $"A cloud provider credential set named '{request.Name}' already exists."
            );

        CloudCredentialValidator.ValidateOrThrow(request.ProviderType, request.Credentials);

        credential.Name = request.Name;
        credential.ProviderType = request.ProviderType;
        credential.CredentialsEncrypted = protector.Protect(request.Credentials);
        credential.IsDisabled = request.IsDisabled;
        credential.Comments = request.Comments;
        credential.UpdatedAtUtc = DateTimeOffset.UtcNow;

        credentialRepository.Update(credential);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This action partially updates an existing cloud DNS provider credential set via RFC
    ///     7396 JSON Merge Patch: a field absent from the patch document is left untouched, and
    ///     any other present field overwrites it. When <c>credentials</c> is present, it replaces
    ///     the entire stored secret-material object atomically (this project treats it as one
    ///     opaque value, not a deep-merged one, matching <see cref="CloudDnsProviderCredentialEntity.CredentialsEncrypted" />'s
    ///     own "opaque payload" shape) -- omit it to rotate only metadata (name, provider type,
    ///     disabled, comments) without touching the stored credentials at all.
    /// </summary>
    [HttpPatch("{id:guid}")]
    [RequirePermission("cloudprovidercredentials:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(CloudProviderCredentialPatchRequestExample))]
    public async Task<IActionResult> Patch(
        Guid id,
        [FromBody] JsonElement patch,
        CancellationToken stoppingToken
    )
    {
        CloudDnsProviderCredentialEntity credential = await credentialRepository.GetByIdAsync(
            id,
            stoppingToken
        );
        if (credential is null)
            throw new NotFoundException("CloudProviderCredential", id.ToString());

        if (patch.TryGetMergePatchField("name", out JsonElement name))
        {
            string newName = name.GetString();
            if (string.IsNullOrWhiteSpace(newName))
                throw new ValidationException("'name' must not be empty.");

            CloudDnsProviderCredentialEntity existingByName =
                await credentialRepository.FindByNameAsync(newName, stoppingToken);
            if (existingByName is not null && existingByName.Id != id)
                throw new ConflictException(
                    $"A cloud provider credential set named '{newName}' already exists."
                );

            credential.Name = newName;
        }

        CloudDnsProviderTypeEnum providerType = credential.ProviderType;
        if (patch.TryGetMergePatchField("providerType", out JsonElement providerTypeElement))
            providerType = JsonSerializer.Deserialize<CloudDnsProviderTypeEnum>(
                providerTypeElement.GetRawText()
            );

        if (patch.TryGetMergePatchField("credentials", out JsonElement credentialsElement))
        {
            Dictionary<string, string> newCredentials = JsonSerializer.Deserialize<
                Dictionary<string, string>
            >(credentialsElement.GetRawText());

            CloudCredentialValidator.ValidateOrThrow(providerType, newCredentials);

            credential.ProviderType = providerType;
            credential.CredentialsEncrypted = protector.Protect(newCredentials);
        }
        else if (providerType != credential.ProviderType)
        {
            // The provider type changed without new credentials to match it -- the existing,
            // still-encrypted credentials almost certainly do not have the right shape for the
            // new provider, so this is rejected rather than silently accepted.
            throw new ValidationException(
                "'providerType' cannot be changed without also supplying a matching 'credentials' object."
            );
        }

        if (patch.TryGetMergePatchField("isDisabled", out JsonElement isDisabled))
            credential.IsDisabled = isDisabled.GetBoolean();

        if (patch.TryGetMergePatchField("comments", out JsonElement comments))
            credential.Comments =
                comments.ValueKind == JsonValueKind.Null ? null : comments.GetString();

        credential.UpdatedAtUtc = DateTimeOffset.UtcNow;
        credentialRepository.Update(credential);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This action permanently deletes a cloud DNS provider credential set. Any zone still
    ///     referencing it by identifier (see <see cref="ZonesController" />'s own
    ///     <c>CloudProviderCredentialId</c>) simply stops being able to sync against it -- this
    ///     project does not track or block on that soft reference, the same laxness
    ///     <see cref="TsigKeysController.Delete" /> already accepts for a TSIG key's own
    ///     <c>TsigKeyName</c> reference.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("cloudprovidercredentials:write")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken stoppingToken)
    {
        CloudDnsProviderCredentialEntity credential = await credentialRepository.GetByIdAsync(
            id,
            stoppingToken
        );
        if (credential is null)
            throw new NotFoundException("CloudProviderCredential", id.ToString());

        credentialRepository.Remove(credential);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This method converts a cloud DNS provider credential entity into its REST
    ///     representation, deliberately never including its own secret material.
    /// </summary>
    private static CloudProviderCredentialDetail ToDetail(
        CloudDnsProviderCredentialEntity credential
    ) =>
        new(
            credential.Id,
            credential.Name,
            credential.ProviderType,
            credential.IsDisabled,
            credential.Comments,
            credential.CreatedAtUtc,
            credential.UpdatedAtUtc
        );
}
