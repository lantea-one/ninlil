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
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller administers the DNSSEC signing keys stored inside a single zone, nested
///     under it (<c>/api/zones/{zoneId}/dnssec-keys</c>) since a key belongs to exactly one zone
///     via <see cref="DnssecKeyEntity.ZoneId" /> -- the same nesting <see cref="RecordsController" />
///     already establishes for a zone's records. Generation and decryption of key material itself
///     goes through <see cref="IDnssecKeyManagementService" />; this controller only ever touches
///     a key's own metadata (retirement status, comments) directly. Every action requires the
///     <c>zones:read</c> or <c>zones:write</c> permission, the same as <see cref="ZonesController" />
///     and <see cref="RecordsController" />. A key's own private key material is never returned by
///     any action here -- see <see cref="DnssecKeyDetail" /> for why.
/// </summary>
/// <param name="zoneRepository">The repository used to confirm the owning zone exists.</param>
/// <param name="dnssecKeyRepository">The repository this controller reads and writes keys through.</param>
/// <param name="dnssecKeyManagementService">
///     The service a new key is actually generated through -- it owns key-pair generation, key
///     tag computation, and private-key encryption; this controller never does any of that itself.
/// </param>
/// <param name="unitOfWork">The unit of work a metadata-only change is committed through.</param>
/// <param name="eventPublisher">
///     The publisher this controller reports a change through once persisted, so
///     <c>SignedZoneCache</c> reloads and re-signs the zone with the updated key set --
///     <see cref="IDnssecKeyManagementService" /> itself publishes no such event, unlike
///     <see cref="Ninlil.Dns.Server.Authoritative.Journal.IZoneUpdateService" />, so this controller must.
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
    ///     This action lists every DNSSEC key belonging to a zone.
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
        if (await zoneRepository.GetByIdAsync(zoneId, stoppingToken) is null)
            throw new NotFoundException("Zone", zoneId.ToString());

        (int skip, int take) = (pagination ?? new PaginationQuery()).Normalize();

        // Both the page itself and its total count are pushed down to the storage engine -- a
        // real, translated ORDER BY/OFFSET/LIMIT (or the MongoDB equivalent) -- rather than
        // materializing every key into memory first.
        IReadOnlyList<DnssecKeyEntity> keys = await dnssecKeyRepository.SearchByZoneIdAsync(
            zoneId,
            skip,
            take,
            stoppingToken
        );
        int totalCount = await dnssecKeyRepository.CountByZoneIdAsync(zoneId, stoppingToken);

        return Ok(
            new PagedResult<DnssecKeyDetail>([.. keys.Select(ToDetail)], skip, take, totalCount)
        );
    }

    /// <summary>
    ///     This action looks up a single DNSSEC key by its identifier.
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

        return Ok(ToDetail(key));
    }

    /// <summary>
    ///     This action generates a brand-new DNSSEC key for the zone, already active.
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

        // DnssecKeyManagementService.GenerateKeyAsync throws ValidationException directly for
        // both an algorithm the server's own configured policy disallows and an algorithm with
        // no registered signing implementation at all -- caught by the shared exception handler,
        // never re-derived here.
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

        return CreatedAtAction(nameof(Get), new { zoneId, id = key.Id }, ToDetail(key));
    }

    /// <summary>
    ///     This action fully replaces an existing key's own mutable state (retirement status and
    ///     comments) -- see <see cref="DnssecKeyWriteRequest" /> for why nothing else is writable.
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

        // IsRetired stays a plain two-way toggle over LifecycleState, exactly as it behaved before
        // this key gained a real RFC 6781 rollover state model: true forces the key straight to
        // Removed (the same outcome IDnssecKeyManagementService.RetireKeyAsync produces), false
        // forces it back to Active -- a deliberate manual override an operator can use to cancel a
        // rollover mid-flight, not something DnssecKeyRolloverBackgroundService's own automated
        // walk ever does on its own.
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
    ///     This action partially updates an existing key's own mutable state via RFC 7396 JSON
    ///     Merge Patch: a field absent from the patch document is left untouched, a field present
    ///     with a JSON <c>null</c> clears it (where the underlying field is itself nullable), and
    ///     any other present field overwrites it.
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

        // See Replace's own comment above for why this remains a plain two-way toggle over
        // LifecycleState.
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
    ///     This action permanently deletes a DNSSEC key. Prefer retiring a key (<see cref="Patch" />
    ///     or <see cref="Replace" /> with <c>isRetired: true</c>) over deleting it outright, unless
    ///     the key is already retired and no longer needed for signature validation continuity.
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
    ///     This action starts a real, automated RFC 6781 key rollover, replacing an existing
    ///     currently-active key -- see <see cref="IDnssecKeyManagementService.StartRolloverAsync" />
    ///     for exactly what happens next, and which scheme (Pre-Publish or Double-Signature) is
    ///     chosen automatically from the existing key's own role.
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

        // StartRolloverAsync throws NotFoundException/ValidationException/ConflictException
        // directly for every real failure mode; none is re-derived here, the same precedent
        // Create's own doc comment already establishes for GenerateKeyAsync.
        DnssecKeyEntity replacement = await dnssecKeyManagementService.StartRolloverAsync(
            zoneId,
            id,
            request?.Algorithm,
            request?.KeySizeBits ?? 0,
            stoppingToken
        );

        // The incoming key is already published in the zone's own DNSKEY RRset the moment it is
        // created (every non-Removed state is), and a Double-Signature KSK already signs -- both
        // real content changes SignedZoneCache must pick up right away.
        eventPublisher.Publish(new DnsZoneChangedEvent(DateTimeOffset.UtcNow, zoneId, zone.Name));

        return CreatedAtAction(
            nameof(Get),
            new { zoneId, id = replacement.Id },
            ToDetail(replacement)
        );
    }

    /// <summary>
    ///     This action records that an operator has confirmed a Key-Signing Key rollover's own new
    ///     DS record has actually been published at a real, externally-hosted parent zone -- see
    ///     <see cref="IDnssecKeyManagementService.ConfirmParentDsUpdatedAsync" /> for exactly when
    ///     this is needed at all.
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

        // ConfirmParentDsUpdatedAsync throws ValidationException directly when the key is not
        // really a KSK mid-rollover -- not re-derived here.
        await dnssecKeyManagementService.ConfirmParentDsUpdatedAsync(id, stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This method converts a DNSSEC key entity into its REST representation, deliberately
    ///     never including its own private key material.
    /// </summary>
    private static DnssecKeyDetail ToDetail(DnssecKeyEntity key) =>
        new(
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
            key.ParentDsConfirmedAtUtc
        );
}
