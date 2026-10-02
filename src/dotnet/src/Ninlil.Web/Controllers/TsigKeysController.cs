using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Tsig;
using Ninlil.Web.Examples.TsigKeys;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller manages TSIG keys (RFC 8945), the shared secrets that zone transfers and
///     dynamic updates authenticate with. A key belongs to no single zone; a zone refers to one by
///     name through its <c>TsigKeyName</c> (see <see cref="ZonesController" />), with no foreign
///     key. The cluster channel's keys are listed here too, but a key created here never
///     authenticates a peer (see <c>TsigKeyEntity.ClusterNodeId</c>). Secrets are encrypted at rest and returned only
///     when created. Reads need <c>tsigkeys:read</c> and changes need <c>tsigkeys:write</c>.
/// </summary>
/// <param name="tsigKeyRepository">Reads and writes keys.</param>
/// <param name="unitOfWork">Commits staged changes.</param>
/// <param name="secretProtector">
///     Encrypts a new key's secret before it is stored; <c>TsigService</c> decrypts it with the
///     same protector when signing or verifying.
/// </param>
[ApiController]
[Route("api/tsig-keys")]
public class TsigKeysController(
    ITsigKeyRepository tsigKeyRepository,
    IUnitOfWork unitOfWork,
    TsigSecretProtector secretProtector
) : ControllerBase
{
    /// <summary>
    ///     This constant contains the length, in bytes, of a generated shared secret: 256 bits, the
    ///     HMAC-SHA256 output size. HMAC-SHA256 is the only algorithm Ninlil implements.
    /// </summary>
    private const int GeneratedSecretLengthBytes = 32;

    /// <summary>
    ///     This property contains the presentation form, without the trailing dot, of the only MAC
    ///     algorithm Ninlil implements, derived from <c>DnsTsigOptions.HmacSha256AlgorithmName</c>.
    /// </summary>
    private static readonly string HmacSha256AlgorithmName = DnsTsigOptions
        .HmacSha256AlgorithmName.ToString()
        .TrimEnd('.');

    /// <summary>
    ///     This action lists TSIG keys, one page at a time, without their secrets. Requires
    ///     <c>tsigkeys:read</c>.
    /// </summary>
    [HttpGet]
    [RequirePermission("tsigkeys:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(TsigKeyDetailPagedResultExample))]
    public async Task<ActionResult<PagedResult<TsigKeyDetail>>> List(
        [FromQuery] PaginationQuery pagination,
        CancellationToken stoppingToken
    )
    {
        (int skip, int take) = (pagination ?? new PaginationQuery()).Normalize();

        // The page and the total count are both computed by the storage engine (ORDER
        // BY/OFFSET/LIMIT or the MongoDB equivalent), not by loading every key into memory.
        IReadOnlyList<TsigKeyEntity> keys = await tsigKeyRepository.SearchAsync(
            skip,
            take,
            stoppingToken
        );
        int totalCount = await tsigKeyRepository.CountAsync(stoppingToken);

        return Ok(
            new PagedResult<TsigKeyDetail>([.. keys.Select(ToDetail)], skip, take, totalCount)
        );
    }

    /// <summary>
    ///     This action returns one TSIG key, without its secret. Returns 404 for an unknown id.
    ///     Requires <c>tsigkeys:read</c>.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("tsigkeys:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(TsigKeyDetailExample))]
    public async Task<ActionResult<TsigKeyDetail>> Get(Guid id, CancellationToken stoppingToken)
    {
        TsigKeyEntity key = await tsigKeyRepository.GetByIdAsync(id, stoppingToken);
        if (key is null)
            throw new NotFoundException("TsigKey", id.ToString());

        return Ok(ToDetail(key));
    }

    /// <summary>
    ///     This action creates a TSIG key. Without <see cref="TsigKeyCreateRequest.Secret" />, a
    ///     random 32-byte secret is generated; without an algorithm, <c>hmac-sha256</c> is used.
    ///     The base64 secret is returned in this response only. Returns 400 for an invalid name, an
    ///     unsupported algorithm or a secret that is not non-empty base64, and 409 when the name is
    ///     taken. Requires <c>tsigkeys:write</c>.
    /// </summary>
    [HttpPost]
    [RequirePermission("tsigkeys:write")]
    [SwaggerRequestExample(typeof(TsigKeyCreateRequest), typeof(TsigKeyCreateRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status201Created, typeof(TsigKeyCreateResponseExample))]
    public async Task<ActionResult<TsigKeyCreateResponse>> Create(
        TsigKeyCreateRequest request,
        CancellationToken stoppingToken
    )
    {
        if (!DnsName.TryParse(request?.Name, out DnsName _))
            throw new ValidationException($"'{request?.Name}' is not a valid TSIG key name.");

        string name = request.Name.TrimEnd('.');

        if (await tsigKeyRepository.FindByNameAsync(name, stoppingToken) is not null)
            throw new ConflictException($"A TSIG key named '{name}' already exists.");

        if (
            !TryResolveAlgorithmName(
                request.AlgorithmName,
                allowDefault: true,
                out string algorithmName,
                out string algorithmError
            )
        )
            throw new ValidationException(algorithmError);

        if (!TryResolveSecret(request.Secret, out byte[] secretBytes, out string secretError))
            throw new ValidationException(secretError);

        TsigKeyEntity key = new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            AlgorithmName = algorithmName,
            SecretEncrypted = secretProtector.Protect(secretBytes),
            IsDisabled = request.IsDisabled,
            Comments = request.Comments,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        await tsigKeyRepository.AddAsync(key, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return CreatedAtAction(
            nameof(Get),
            new { id = key.Id },
            new TsigKeyCreateResponse(ToDetail(key), Convert.ToBase64String(secretBytes))
        );
    }

    /// <summary>
    ///     This action replaces a TSIG key's name, algorithm, disabled flag and comments. The
    ///     secret is left unchanged (see <see cref="TsigKeyWriteRequest" />). Returns 400 for an
    ///     invalid name or algorithm, 404 for an unknown id and 409 when the name is taken.
    ///     Requires <c>tsigkeys:write</c>.
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("tsigkeys:write")]
    [SwaggerRequestExample(typeof(TsigKeyWriteRequest), typeof(TsigKeyWriteRequestExample))]
    public async Task<IActionResult> Replace(
        Guid id,
        TsigKeyWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        TsigKeyEntity key = await tsigKeyRepository.GetByIdAsync(id, stoppingToken);
        if (key is null)
            throw new NotFoundException("TsigKey", id.ToString());

        if (!DnsName.TryParse(request?.Name, out DnsName _))
            throw new ValidationException($"'{request?.Name}' is not a valid TSIG key name.");

        string name = request.Name.TrimEnd('.');

        TsigKeyEntity existingByName = await tsigKeyRepository.FindByNameAsync(name, stoppingToken);
        if (existingByName is not null && existingByName.Id != id)
            throw new ConflictException($"A TSIG key named '{name}' already exists.");

        if (
            !TryResolveAlgorithmName(
                request.AlgorithmName,
                allowDefault: false,
                out string algorithmName,
                out string algorithmError
            )
        )
            throw new ValidationException(algorithmError);

        key.Name = name;
        key.AlgorithmName = algorithmName;
        key.IsDisabled = request.IsDisabled;
        key.Comments = request.Comments;
        key.UpdatedAtUtc = DateTimeOffset.UtcNow;

        tsigKeyRepository.Update(key);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This action updates a TSIG key as an RFC 7396 merge patch: an absent field is unchanged,
    ///     a JSON <c>null</c> clears the comments, and any other value replaces it. The secret
    ///     cannot be changed. Errors are as for <see cref="Replace" />. Requires
    ///     <c>tsigkeys:write</c>.
    /// </summary>
    [HttpPatch("{id:guid}")]
    [RequirePermission("tsigkeys:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(TsigKeyPatchRequestExample))]
    public async Task<IActionResult> Patch(
        Guid id,
        [FromBody] JsonElement patch,
        CancellationToken stoppingToken
    )
    {
        TsigKeyEntity key = await tsigKeyRepository.GetByIdAsync(id, stoppingToken);
        if (key is null)
            throw new NotFoundException("TsigKey", id.ToString());

        if (patch.TryGetMergePatchField("name", out JsonElement nameElement))
        {
            string newName = nameElement.GetString();
            if (!DnsName.TryParse(newName, out DnsName _))
                throw new ValidationException($"'{newName}' is not a valid TSIG key name.");

            newName = newName.TrimEnd('.');
            TsigKeyEntity existingByName = await tsigKeyRepository.FindByNameAsync(
                newName,
                stoppingToken
            );
            if (existingByName is not null && existingByName.Id != id)
                throw new ConflictException($"A TSIG key named '{newName}' already exists.");

            key.Name = newName;
        }

        if (patch.TryGetMergePatchField("algorithmName", out JsonElement algorithmNameElement))
        {
            if (
                !TryResolveAlgorithmName(
                    algorithmNameElement.GetString(),
                    allowDefault: false,
                    out string algorithmName,
                    out string algorithmError
                )
            )
                throw new ValidationException(algorithmError);

            key.AlgorithmName = algorithmName;
        }

        if (patch.TryGetMergePatchField("isDisabled", out JsonElement isDisabled))
            key.IsDisabled = isDisabled.GetBoolean();

        if (patch.TryGetMergePatchField("comments", out JsonElement comments))
            key.Comments = comments.ValueKind == JsonValueKind.Null ? null : comments.GetString();

        key.UpdatedAtUtc = DateTimeOffset.UtcNow;
        tsigKeyRepository.Update(key);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This action deletes a TSIG key. Zones that still refer to it by name are not checked or
    ///     blocked; they simply can no longer sign or verify with it. Returns 404 for an unknown
    ///     id. Requires <c>tsigkeys:write</c>.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("tsigkeys:write")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken stoppingToken)
    {
        TsigKeyEntity key = await tsigKeyRepository.GetByIdAsync(id, stoppingToken);
        if (key is null)
            throw new NotFoundException("TsigKey", id.ToString());

        tsigKeyRepository.Remove(key);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This method checks an algorithm name against the only algorithm Ninlil implements,
    ///     defaulting to it when none is given and <paramref name="allowDefault" /> is set.
    /// </summary>
    private static bool TryResolveAlgorithmName(
        string requestedAlgorithmName,
        bool allowDefault,
        out string algorithmName,
        out string error
    )
    {
        algorithmName = null;
        error = null;

        if (string.IsNullOrWhiteSpace(requestedAlgorithmName))
        {
            if (!allowDefault)
            {
                error = "'algorithmName' is required.";
                return false;
            }

            algorithmName = HmacSha256AlgorithmName;
            return true;
        }

        if (
            !requestedAlgorithmName
                .TrimEnd('.')
                .Equals(HmacSha256AlgorithmName, StringComparison.OrdinalIgnoreCase)
        )
        {
            error =
                $"'{requestedAlgorithmName}' is not a supported TSIG algorithm; only '{HmacSha256AlgorithmName}' is implemented.";
            return false;
        }

        algorithmName = HmacSha256AlgorithmName;
        return true;
    }

    /// <summary>
    ///     This method resolves a new key's secret: the caller's base64-encoded secret if given,
    ///     otherwise a new random one.
    /// </summary>
    private static bool TryResolveSecret(
        string requestedSecret,
        out byte[] secret,
        out string error
    )
    {
        error = null;

        if (string.IsNullOrWhiteSpace(requestedSecret))
        {
            secret = RandomNumberGenerator.GetBytes(GeneratedSecretLengthBytes);
            return true;
        }

        try
        {
            secret = Convert.FromBase64String(requestedSecret);
        }
        catch (FormatException)
        {
            secret = null;
            error = "'secret' must be base64-encoded.";
            return false;
        }

        if (secret.Length == 0)
        {
            error = "'secret' must not be empty.";
            return false;
        }

        return true;
    }

    /// <summary>
    ///     This method converts a key entity to its API representation, which never includes the
    ///     secret.
    /// </summary>
    private static TsigKeyDetail ToDetail(TsigKeyEntity key) =>
        new(
            key.Id,
            key.Name,
            key.AlgorithmName,
            key.IsDisabled,
            key.Comments,
            key.CreatedAtUtc,
            key.UpdatedAtUtc
        );
}
