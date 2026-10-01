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
///     This controller manages cloud DNS provider credential sets: the Cloudflare, Route 53 or
///     DigitalOcean API credentials that a <c>CloudPrimary</c> or <c>CloudSecondary</c> zone's
///     connector uses (see <c>CloudDnsZoneSyncService</c>). A credential set belongs to no single
///     zone; several zones can share one account's credentials, and each refers to it by id
///     through its <c>CloudProviderCredentialId</c> (see <see cref="ZonesController" />), as zones
///     refer to <see cref="TsigKeysController" /> keys by name. Secrets are encrypted at rest and
///     returned only when created. Reads need <c>cloudprovidercredentials:read</c> and changes
///     need <c>cloudprovidercredentials:write</c>.
/// </summary>
/// <param name="credentialRepository">Reads and writes credential sets.</param>
/// <param name="unitOfWork">Commits staged changes.</param>
/// <param name="protector">
///     Encrypts a credential set's secrets before they are stored; <c>CloudDnsZoneSyncService</c>
///     decrypts them with the same protector at sync time.
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
    ///     This action lists credential sets, one page at a time, without their secrets. Requires
    ///     <c>cloudprovidercredentials:read</c>.
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

        // The page and the total count are both computed by the storage engine (ORDER
        // BY/OFFSET/LIMIT or the MongoDB equivalent), not by loading every credential set into
        // memory.
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
    ///     This action returns one credential set, without its secrets. Returns 404 for an unknown
    ///     id. Requires <c>cloudprovidercredentials:read</c>.
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
    ///     This action creates a credential set. The plaintext secrets are echoed back in this
    ///     response only; later reads never include them. Returns 400 for a missing name or
    ///     credentials that do not fit the provider type, and 409 when the name is taken. Requires
    ///     <c>cloudprovidercredentials:write</c>.
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
    ///     This action replaces a credential set, secrets included (see
    ///     <see cref="CloudProviderCredentialWriteRequest" /> for how this differs from
    ///     <see cref="TsigKeysController.Replace" />). Validation and errors are as for
    ///     <see cref="Create" />, plus 404 for an unknown id. Requires
    ///     <c>cloudprovidercredentials:write</c>.
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
    ///     This action updates a credential set as an RFC 7396 merge patch: an absent field is
    ///     unchanged and a present one replaces it. <c>credentials</c>, when present, replaces the
    ///     whole stored secret object, which is treated as one opaque value and never merged; omit
    ///     it to change only the name, disabled flag or comments. Changing <c>providerType</c>
    ///     requires matching <c>credentials</c> in the same patch. Requires
    ///     <c>cloudprovidercredentials:write</c>.
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
            // The stored credentials almost certainly do not fit the new provider, so a provider
            // change without new credentials is rejected.
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
    ///     This action deletes a credential set. Zones that still refer to it by
    ///     <c>CloudProviderCredentialId</c> are not checked or blocked; they simply can no longer
    ///     sync, as with a deleted TSIG key (see <see cref="TsigKeysController.Delete" />).
    ///     Requires <c>cloudprovidercredentials:write</c>.
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
    ///     This method converts a credential entity to its API representation, which never
    ///     includes the secrets.
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
