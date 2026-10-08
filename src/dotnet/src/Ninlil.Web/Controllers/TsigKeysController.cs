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
///     key. The cluster channel's keys are listed here too, flagged <c>isClusterKey</c>: a key
///     created here never authenticates a peer (see <c>TsigKeyEntity.ClusterNodeId</c>), and a
///     cluster key can be disabled or commented but not renamed, re-keyed to another algorithm or
///     deleted (clustering owns it;
///     removing the peer removes its key). Key names are stored in lowercase. Secrets are
///     encrypted at rest and returned only when created. Reads need <c>tsigkeys:read</c> and
///     changes need <c>tsigkeys:write</c>.
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
    ///     This property contains the algorithm a new key gets when the request names none:
    ///     HMAC-SHA256, the RFC 8945 §6 mandatory algorithm that BIND also defaults to.
    /// </summary>
    private static readonly TsigAlgorithm DefaultAlgorithm = TsigAlgorithm.Find(
        DnsTsigOptions.HmacSha256AlgorithmName
    );

    /// <summary>
    ///     This constant contains the conflict message for a rename or delete of a cluster key.
    /// </summary>
    private const string ClusterKeyMessage =
        "This TSIG key is managed by clustering; remove the peer, or let the invite expire.";

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
    ///     random secret as long as the algorithm's MAC (32 bytes for the default) is generated; without an
    ///     algorithm, <c>hmac-sha256</c> is used. The name is stored in lowercase.
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
        string name = ParseName(request?.Name);

        if (await tsigKeyRepository.FindByNameAsync(name, stoppingToken) is not null)
            throw new ConflictException($"A TSIG key named '{name}' already exists.");

        if (
            !TryResolveAlgorithm(
                request.AlgorithmName,
                allowDefault: true,
                out TsigAlgorithm algorithm,
                out string algorithmError
            )
        )
            throw new ValidationException(algorithmError);

        string algorithmName = FormatAlgorithmName(algorithm);

        if (
            !TryResolveSecret(
                request.Secret,
                algorithm,
                out byte[] secretBytes,
                out string secretError
            )
        )
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
    ///     invalid name or algorithm, 404 for an unknown id, and 409 when the name is taken or when a
    ///     cluster key would be renamed or change algorithm.
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

        string name = ParseName(request?.Name);

        // A key's own spelling may differ from the lowercase form (rows are normalised at
        // startup, but a restored backup can bring old spellings back), so only a different
        // lowercase name counts as a rename.
        bool isRename = !string.Equals(key.Name, name, StringComparison.Ordinal);
        if (isRename && IsClusterKey(key))
            throw new ConflictException(ClusterKeyMessage);

        TsigKeyEntity existingByName = await tsigKeyRepository.FindByNameAsync(name, stoppingToken);
        if (existingByName is not null && existingByName.Id != id)
            throw new ConflictException($"A TSIG key named '{name}' already exists.");

        if (
            !TryResolveAlgorithm(
                request.AlgorithmName,
                allowDefault: false,
                out TsigAlgorithm algorithm,
                out string algorithmError
            )
        )
            throw new ValidationException(algorithmError);

        // The cluster channel signs with the algorithm clustering chose, so it stays fixed too.
        if (IsClusterKey(key) && TsigAlgorithm.Find(key.AlgorithmName) != algorithm)
            throw new ConflictException(ClusterKeyMessage);

        key.Name = name;
        key.AlgorithmName = FormatAlgorithmName(algorithm);
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
            string newName = ParseName(nameElement.GetString());
            if (!string.Equals(key.Name, newName, StringComparison.Ordinal) && IsClusterKey(key))
                throw new ConflictException(ClusterKeyMessage);

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
                !TryResolveAlgorithm(
                    algorithmNameElement.GetString(),
                    allowDefault: false,
                    out TsigAlgorithm algorithm,
                    out string algorithmError
                )
            )
                throw new ValidationException(algorithmError);

            if (IsClusterKey(key) && TsigAlgorithm.Find(key.AlgorithmName) != algorithm)
                throw new ConflictException(ClusterKeyMessage);

            key.AlgorithmName = FormatAlgorithmName(algorithm);
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
    ///     id and 409 for a cluster key (see the class summary). Requires <c>tsigkeys:write</c>.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("tsigkeys:write")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken stoppingToken)
    {
        TsigKeyEntity key = await tsigKeyRepository.GetByIdAsync(id, stoppingToken);
        if (key is null)
            throw new NotFoundException("TsigKey", id.ToString());

        if (IsClusterKey(key))
            throw new ConflictException(ClusterKeyMessage);

        tsigKeyRepository.Remove(key);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This method validates a key name and returns its stored form: the canonical lowercase
    ///     presentation without the trailing dot. DNS names compare without case (RFC 4343), and
    ///     BIND lowercases a key name on the wire, so a mixed-case stored name would never match a
    ///     BIND client's signature.
    /// </summary>
    /// <exception cref="ValidationException">The name is not a valid, non-root domain name.</exception>
    private static string ParseName(string requestedName)
    {
        if (!DnsName.TryParse(requestedName, out DnsName parsed))
            throw new ValidationException($"'{requestedName}' is not a valid TSIG key name.");

        string name = parsed.ToCanonicalLower().ToString().TrimEnd('.');
        if (name.Length == 0)
            throw new ValidationException($"'{requestedName}' is not a valid TSIG key name.");

        return name;
    }

    /// <summary>
    ///     This method tells whether clustering owns a key: a peer's channel key, or an invite key
    ///     not used up yet. This is the same test <c>TsigService</c> uses to keep such keys away
    ///     from DNS messages.
    /// </summary>
    private static bool IsClusterKey(TsigKeyEntity key) =>
        key.ClusterNodeId is not null || key.ClusterInviteExpiresAtUtc is not null;

    /// <summary>
    ///     This method returns the stored spelling of an algorithm: its name without the trailing
    ///     dot (<c>hmac-sha256</c>).
    /// </summary>
    private static string FormatAlgorithmName(TsigAlgorithm algorithm) =>
        algorithm.Name.ToString().TrimEnd('.');

    /// <summary>
    ///     This method resolves an algorithm name to a supported algorithm, defaulting to
    ///     HMAC-SHA256 when none is given and <paramref name="allowDefault" /> is set. Names
    ///     compare without case and with or without the trailing dot.
    /// </summary>
    private static bool TryResolveAlgorithm(
        string requestedAlgorithmName,
        bool allowDefault,
        out TsigAlgorithm algorithm,
        out string error
    )
    {
        algorithm = null;
        error = null;

        if (string.IsNullOrWhiteSpace(requestedAlgorithmName))
        {
            if (!allowDefault)
            {
                error = "'algorithmName' is required.";
                return false;
            }

            algorithm = DefaultAlgorithm;
            return true;
        }

        algorithm = TsigAlgorithm.Find(requestedAlgorithmName);
        if (algorithm is null)
        {
            string supported = string.Join(
                ", ",
                TsigAlgorithm.Supported.Select(FormatAlgorithmName)
            );
            error =
                $"'{requestedAlgorithmName}' is not a supported TSIG algorithm; use one of {supported}.";
            return false;
        }

        return true;
    }

    /// <summary>
    ///     This method resolves a new key's secret: the caller's base64-encoded secret if given,
    ///     otherwise a new random one as long as the algorithm's MAC (RFC 2104 §3 recommends a key
    ///     at least that long).
    /// </summary>
    private static bool TryResolveSecret(
        string requestedSecret,
        TsigAlgorithm algorithm,
        out byte[] secret,
        out string error
    )
    {
        error = null;

        if (string.IsNullOrWhiteSpace(requestedSecret))
        {
            secret = RandomNumberGenerator.GetBytes(algorithm.DigestLength);
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
            IsClusterKey(key),
            key.Comments,
            key.CreatedAtUtc,
            key.UpdatedAtUtc
        );
}
