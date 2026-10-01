using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Dnssec;
using Ninlil.Web.Examples.DnssecKeys;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Services;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller manages one zone's DNSSEC signing keys, nested under it at
///     <c>/api/zones/{zoneId}/dnssec-keys</c> because each key belongs to one zone through
///     <see cref="DnssecKeyEntity.ZoneId" />. Generating keys, starting rollovers and confirming
///     parent DS updates go through <see cref="IDnssecKeyManagementService" />; this controller
///     edits only a key's retirement state and comments itself. Reads need <c>zones:read</c> and
///     changes need <c>zones:write</c>, as for <see cref="ZonesController" />. Private key
///     material is never returned (see <see cref="DnssecKeyDetail" />).
/// </summary>
/// <param name="zoneRepository">Confirms the zone exists and supplies its name.</param>
/// <param name="dnssecKeyRepository">Reads and writes keys.</param>
/// <param name="dnssecKeyManagementService">
///     Generates keys (key pair, key tag, private-key encryption) and runs rollovers.
/// </param>
/// <param name="unitOfWork">Commits metadata-only changes.</param>
/// <param name="eventPublisher">
///     Publishes <see cref="DnsZoneChangedEvent" /> after a change so <c>SignedZoneCache</c>
///     re-signs the zone with the new key set. <see cref="IDnssecKeyManagementService" /> does not
///     publish it itself (unlike
///     <see cref="Ninlil.Dns.Server.Authoritative.Journal.IZoneUpdateService" />), so this
///     controller does.
/// </param>
[ApiController]
[Route("api/zones/{zoneId:guid}/dnssec-keys")]
public class DnssecKeysController(
    IZoneRepository zoneRepository,
    IDnssecKeyRepository dnssecKeyRepository,
    IDnssecKeyManagementService dnssecKeyManagementService,
    IUnitOfWork unitOfWork,
    IDnsServerEventPublisher eventPublisher
) : ControllerBase
{
    /// <summary>
    ///     This action lists a zone's DNSSEC keys, one page at a time, each with its DNSKEY and DS
    ///     record text. Returns 404 for an unknown zone. Requires <c>zones:read</c>.
    /// </summary>
    [HttpGet]
    [RequirePermission("zones:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DnssecKeyDetailPagedResultExample))]
    public async Task<ActionResult<PagedResult<DnssecKeyDetail>>> List(
        Guid zoneId,
        [FromQuery] PaginationQuery pagination,
        CancellationToken stoppingToken
    )
    {
        ZoneEntity zone = await zoneRepository.GetByIdAsync(zoneId, stoppingToken);
        if (zone is null)
            throw new NotFoundException("Zone", zoneId.ToString());

        (int skip, int take) = (pagination ?? new PaginationQuery()).Normalize();

        // The page and the total count are both computed by the storage engine (ORDER
        // BY/OFFSET/LIMIT or the MongoDB equivalent), not by loading every key into memory.
        IReadOnlyList<DnssecKeyEntity> keys = await dnssecKeyRepository.SearchByZoneIdAsync(
            zoneId,
            skip,
            take,
            stoppingToken
        );
        int totalCount = await dnssecKeyRepository.CountByZoneIdAsync(zoneId, stoppingToken);

        return Ok(
            new PagedResult<DnssecKeyDetail>(
                [.. keys.Select(key => ToDetail(key, zone.Name))],
                skip,
                take,
                totalCount
            )
        );
    }

    /// <summary>
    ///     This action returns one DNSSEC key with its DNSKEY and DS record text. Returns 404 when
    ///     the key does not exist or belongs to another zone. Requires <c>zones:read</c>.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("zones:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DnssecKeyDetailExample))]
    public async Task<ActionResult<DnssecKeyDetail>> Get(
        Guid zoneId,
        Guid id,
        CancellationToken stoppingToken
    )
    {
        DnssecKeyEntity key = await dnssecKeyRepository.GetByIdAsync(id, stoppingToken);
        if (key is null || key.ZoneId != zoneId)
            throw new NotFoundException("DnssecKey", id.ToString());

        // The zone's name is the owner name of the DNSKEY and DS records in the response.
        ZoneEntity zone = await zoneRepository.GetByIdAsync(zoneId, stoppingToken);
        if (zone is null)
            throw new NotFoundException("Zone", zoneId.ToString());

        return Ok(ToDetail(key, zone.Name));
    }

    /// <summary>
    ///     This action generates a new DNSSEC key for the zone, active at once, and has the zone
    ///     re-signed. Returns 400 for an algorithm the server's policy disallows or cannot sign
    ///     with, and 404 for an unknown zone. Requires <c>zones:write</c>.
    /// </summary>
    [HttpPost]
    [RequirePermission("zones:write")]
    [SwaggerRequestExample(typeof(DnssecKeyCreateRequest), typeof(DnssecKeyCreateRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status201Created, typeof(DnssecKeyDetailExample))]
    public async Task<ActionResult<DnssecKeyDetail>> Create(
        Guid zoneId,
        DnssecKeyCreateRequest request,
        CancellationToken stoppingToken
    )
    {
        ZoneEntity zone = await zoneRepository.GetByIdAsync(zoneId, stoppingToken);
        if (zone is null)
            throw new NotFoundException("Zone", zoneId.ToString());

        // GenerateKeyAsync throws ValidationException itself for an algorithm the configured policy
        // disallows or that has no signing implementation; the shared exception handler turns it
        // into a 400.
        DnssecKeyEntity key = await dnssecKeyManagementService.GenerateKeyAsync(
            zoneId,
            request.Role,
            request.Algorithm,
            request.KeySizeBits,
            stoppingToken
        );

        if (!string.IsNullOrWhiteSpace(request.Comments))
        {
            key.Comments = request.Comments;
            dnssecKeyRepository.Update(key);
            await unitOfWork.SaveChangesAsync(stoppingToken);
        }

        // SignedZoneCache reloads and re-signs the zone with the new key included.
        eventPublisher.Publish(new DnsZoneChangedEvent(DateTimeOffset.UtcNow, zoneId, zone.Name));

        return CreatedAtAction(nameof(Get), new { zoneId, id = key.Id }, ToDetail(key, zone.Name));
    }

    /// <summary>
    ///     This action replaces a key's editable state, its retirement flag and comments (see
    ///     <see cref="DnssecKeyWriteRequest" /> for why nothing else can be written). Returns 404
    ///     when the zone or key does not exist. Requires <c>zones:write</c>.
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("zones:write")]
    [SwaggerRequestExample(typeof(DnssecKeyWriteRequest), typeof(DnssecKeyWriteRequestExample))]
    public async Task<IActionResult> Replace(
        Guid zoneId,
        Guid id,
        DnssecKeyWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        ZoneEntity zone = await zoneRepository.GetByIdAsync(zoneId, stoppingToken);
        if (zone is null)
            throw new NotFoundException("Zone", zoneId.ToString());

        DnssecKeyEntity key = await dnssecKeyRepository.GetByIdAsync(id, stoppingToken);
        if (key is null || key.ZoneId != zoneId)
            throw new NotFoundException("DnssecKey", id.ToString());

        // IsRetired is a two-way manual override of LifecycleState: true moves the key straight to
        // Removed (as IDnssecKeyManagementService.RetireKeyAsync does) and false moves it back to
        // Active, whatever rollover state it was in. An operator can use it to cancel a rollover;
        // DnssecKeyRolloverBackgroundService never does this itself.
        key.LifecycleState = request.IsRetired
            ? DnssecKeyLifecycleStateEnum.Removed
            : DnssecKeyLifecycleStateEnum.Active;
        key.Comments = request.Comments;
        key.UpdatedAtUtc = DateTimeOffset.UtcNow;

        dnssecKeyRepository.Update(key);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        eventPublisher.Publish(new DnsZoneChangedEvent(DateTimeOffset.UtcNow, zoneId, zone.Name));

        return NoContent();
    }

    /// <summary>
    ///     This action updates a key's retirement flag and comments as an RFC 7396 merge patch: an
    ///     absent field is unchanged, a JSON <c>null</c> clears the comments, and any other value
    ///     replaces it. Returns 404 when the zone or key does not exist. Requires
    ///     <c>zones:write</c>.
    /// </summary>
    [HttpPatch("{id:guid}")]
    [RequirePermission("zones:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(DnssecKeyPatchRequestExample))]
    public async Task<IActionResult> Patch(
        Guid zoneId,
        Guid id,
        [FromBody] JsonElement patch,
        CancellationToken stoppingToken
    )
    {
        ZoneEntity zone = await zoneRepository.GetByIdAsync(zoneId, stoppingToken);
        if (zone is null)
            throw new NotFoundException("Zone", zoneId.ToString());

        DnssecKeyEntity key = await dnssecKeyRepository.GetByIdAsync(id, stoppingToken);
        if (key is null || key.ZoneId != zoneId)
            throw new NotFoundException("DnssecKey", id.ToString());

        // See the comment in Replace: isRetired is a two-way override of LifecycleState.
        if (patch.TryGetMergePatchField("isRetired", out JsonElement isRetired))
            key.LifecycleState = isRetired.GetBoolean()
                ? DnssecKeyLifecycleStateEnum.Removed
                : DnssecKeyLifecycleStateEnum.Active;

        if (patch.TryGetMergePatchField("comments", out JsonElement comments))
            key.Comments = comments.ValueKind == JsonValueKind.Null ? null : comments.GetString();

        key.UpdatedAtUtc = DateTimeOffset.UtcNow;
        dnssecKeyRepository.Update(key);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        eventPublisher.Publish(new DnsZoneChangedEvent(DateTimeOffset.UtcNow, zoneId, zone.Name));

        return NoContent();
    }

    /// <summary>
    ///     This action deletes a DNSSEC key. Prefer retiring a key (<see cref="Patch" /> or
    ///     <see cref="Replace" /> with <c>isRetired: true</c>) unless it is already retired and no
    ///     longer needed to keep signatures validating. Returns 404 when the zone or key does not
    ///     exist. Requires <c>zones:write</c>.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("zones:write")]
    public async Task<IActionResult> Delete(Guid zoneId, Guid id, CancellationToken stoppingToken)
    {
        ZoneEntity zone = await zoneRepository.GetByIdAsync(zoneId, stoppingToken);
        if (zone is null)
            throw new NotFoundException("Zone", zoneId.ToString());

        DnssecKeyEntity key = await dnssecKeyRepository.GetByIdAsync(id, stoppingToken);
        if (key is null || key.ZoneId != zoneId)
            throw new NotFoundException("DnssecKey", id.ToString());

        dnssecKeyRepository.Remove(key);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        eventPublisher.Publish(new DnsZoneChangedEvent(DateTimeOffset.UtcNow, zoneId, zone.Name));

        return NoContent();
    }

    /// <summary>
    ///     This action starts an automated RFC 6781 rollover of an active key and returns the new
    ///     key. The scheme (Pre-Publish or Double-Signature) follows from the key's role; see
    ///     <see cref="IDnssecKeyManagementService.StartRolloverAsync" /> for the steps. Returns
    ///     400, 404 or 409 as that method reports. Requires <c>zones:write</c>.
    /// </summary>
    [HttpPost("{id:guid}/roll")]
    [RequirePermission("zones:write")]
    [SwaggerRequestExample(
        typeof(DnssecKeyRolloverRequest),
        typeof(DnssecKeyRolloverRequestExample)
    )]
    [SwaggerResponseExample(StatusCodes.Status201Created, typeof(DnssecKeyDetailExample))]
    public async Task<ActionResult<DnssecKeyDetail>> Roll(
        Guid zoneId,
        Guid id,
        DnssecKeyRolloverRequest request,
        CancellationToken stoppingToken
    )
    {
        ZoneEntity zone = await zoneRepository.GetByIdAsync(zoneId, stoppingToken);
        if (zone is null)
            throw new NotFoundException("Zone", zoneId.ToString());

        // StartRolloverAsync throws NotFoundException, ValidationException or ConflictException
        // itself for each failure; the shared exception handler maps them.
        DnssecKeyEntity replacement = await dnssecKeyManagementService.StartRolloverAsync(
            zoneId,
            id,
            request?.Algorithm,
            request?.KeySizeBits ?? 0,
            stoppingToken
        );

        // The new key appears in the zone's DNSKEY RRset as soon as it is created (every state but
        // Removed is published), and a Double-Signature KSK signs at once, so the zone must be
        // re-signed now.
        eventPublisher.Publish(new DnsZoneChangedEvent(DateTimeOffset.UtcNow, zoneId, zone.Name));

        return CreatedAtAction(
            nameof(Get),
            new { zoneId, id = replacement.Id },
            ToDetail(replacement, zone.Name)
        );
    }

    /// <summary>
    ///     This action records that the operator has published a Key-Signing Key rollover's new DS
    ///     record at an externally hosted parent zone, so the rollover can continue (see
    ///     <see cref="IDnssecKeyManagementService.ConfirmParentDsUpdatedAsync" />). Returns 400
    ///     when the key is not a KSK in the middle of a rollover and 404 when it does not exist in
    ///     this zone. Requires <c>zones:write</c>.
    /// </summary>
    [HttpPost("{id:guid}/confirm-parent-ds-updated")]
    [RequirePermission("zones:write")]
    public async Task<IActionResult> ConfirmParentDsUpdated(
        Guid zoneId,
        Guid id,
        CancellationToken stoppingToken
    )
    {
        DnssecKeyEntity key = await dnssecKeyRepository.GetByIdAsync(id, stoppingToken);
        if (key is null || key.ZoneId != zoneId)
            throw new NotFoundException("DnssecKey", id.ToString());

        // ConfirmParentDsUpdatedAsync throws ValidationException itself when the key is not a KSK
        // in the middle of a rollover.
        await dnssecKeyManagementService.ConfirmParentDsUpdatedAsync(id, stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This method converts a key entity to its API representation, with its DNSKEY and DS
    ///     record text, and never its private key material.
    /// </summary>
    private static DnssecKeyDetail ToDetail(DnssecKeyEntity key, string zoneName)
    {
        (string dnsKeyRecord, string dsRecord, string dsDigest) = DnssecParentRecords.Describe(
            zoneName,
            key.Role,
            key.Algorithm,
            key.PublicKeyWireFormat,
            key.KeyTag
        );

        return new(
            key.Id,
            key.ZoneId,
            key.Role,
            key.Algorithm,
            Convert.ToBase64String(key.PublicKeyWireFormat),
            key.KeyTag,
            key.LifecycleState == DnssecKeyLifecycleStateEnum.Removed,
            key.Comments,
            key.CreatedAtUtc,
            key.UpdatedAtUtc,
            key.LifecycleState,
            key.PublishedAtUtc,
            key.RetiredAtUtc,
            key.ParentDsConfirmedAtUtc,
            dnsKeyRecord,
            dsRecord,
            dsDigest
        );
    }
}
