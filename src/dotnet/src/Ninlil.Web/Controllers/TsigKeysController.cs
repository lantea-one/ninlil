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
///     This controller administers this server's own TSIG keys -- the pre-shared secrets zone
///     transfers and dynamic updates authenticate with (RFC 8945). A key is not scoped to any
///     one zone; a zone references one by name, through its own <c>TsigKeyName</c> field (see
///     <see cref="ZonesController" />), rather than through a foreign key. Every action requires
///     the <c>tsigkeys:read</c> or <c>tsigkeys:write</c> permission.
/// </summary>
/// <param name="tsigKeyRepository">The repository this controller reads and writes keys through.</param>
/// <param name="unitOfWork">The unit of work every staged change is committed through.</param>
/// <param name="secretProtector">
///     The protector this controller encrypts a new or rotated shared secret through before it is
///     ever persisted -- the same protector <c>TsigService</c> itself decrypts a secret through at
///     verification/signing time.
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
    ///     This constant contains the length, in bytes, of a server-generated shared secret --
    ///     matching HMAC-SHA256's own 256-bit block size, the only algorithm Ninlil implements.
    /// </summary>
    private const int GeneratedSecretLengthBytes = 32;

    /// <summary>
    ///     This property contains the canonical, dot-stripped presentation form of the only MAC
    ///     algorithm Ninlil implements, derived from <c>DnsTsigOptions.HmacSha256AlgorithmName</c>
    ///     itself rather than a separately hand-maintained literal.
    /// </summary>
    private static readonly string HmacSha256AlgorithmName = DnsTsigOptions
        .HmacSha256AlgorithmName.ToString()
        .TrimEnd('.');

    /// <summary>
    ///     This action lists every TSIG key this server knows about.
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

        // Both the page itself and its total count are pushed down to the storage engine -- a
        // real, translated ORDER BY/OFFSET/LIMIT (or the MongoDB equivalent) -- rather than
        // materializing every key into memory first.
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
    ///     This action looks up a single TSIG key by its identifier.
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
    ///     This action creates a new TSIG key. When the caller does not supply
    ///     <see cref="TsigKeyCreateRequest.Secret" />, a cryptographically random secret is
    ///     generated instead. Either way, the plaintext secret is returned exactly once, in this
    ///     action's own response -- every later request for this key omits it entirely.
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
    ///     This action fully replaces an existing TSIG key's metadata. The key's own shared
    ///     secret is left untouched -- see <see cref="TsigKeyWriteRequest" /> for why.
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
    ///     This action partially updates an existing TSIG key's metadata via RFC 7396 JSON Merge
    ///     Patch: a field absent from the patch document is left untouched, a field present with
    ///     a JSON <c>null</c> clears it (where the underlying field is itself nullable), and any
    ///     other present field overwrites it. As with <see cref="Replace" />, the key's own
    ///     shared secret cannot be changed this way.
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
    ///     This action permanently deletes a TSIG key. Any zone still referencing it by name (see
    ///     <see cref="ZonesController" />) simply stops being able to sign or verify transfers
    ///     against it -- this project does not track or block on that soft reference, the same
    ///     laxness <see cref="ZonesController" /> itself already accepts for <c>TsigKeyName</c>.
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
    ///     This method validates a caller-supplied algorithm name against the only algorithm
    ///     Ninlil implements, optionally defaulting to it when none was supplied at all.
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
    ///     This method resolves the shared secret a new key is created with: the caller's own
    ///     base64-encoded secret, if supplied, or a freshly generated random one otherwise.
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
    ///     This method converts a TSIG key entity into its REST representation, deliberately
    ///     never including its own shared secret.
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
