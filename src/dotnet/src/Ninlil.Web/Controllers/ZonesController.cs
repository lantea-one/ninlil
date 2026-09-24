using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Acme.Caa;
using Ninlil.Dns.Server.Authoritative.CatalogSync;
using Ninlil.Dns.Server.Authoritative.Ptr;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Web.Examples.Zones;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller administers every DNS zone this server hosts or references -- creating,
///     updating, and deleting a zone's own metadata (its name, role, DNSSEC posture, transfer and
///     forwarding configuration). A zone's actual record content is managed separately, through
///     the nested <see cref="RecordsController" />. Every action requires the <c>zones:read</c> or
///     <c>zones:write</c> permission.
/// </summary>
/// <param name="zoneRepository">The repository this controller reads and writes zones through.</param>
/// <param name="recordRepository">
///     The repository used to remove a deleted zone's own records, and to confirm a zone is not
///     entirely empty when a caller flips it into a state that would otherwise leave it
///     unanswerable.
/// </param>
/// <param name="dnssecKeyRepository">
///     The repository used to remove a deleted zone's own DNSSEC keys (see
///     <see cref="DnssecKeysController" />) -- <see cref="Ninlil.Dns.Data.Abstractions.Entity.DnssecKeyEntity" />
///     references its zone by <c>ZoneId</c> alone, with no cascade-delete this controller can rely
///     on (MongoDB has no equivalent mechanism at all), so it is removed explicitly here, the same
///     way a zone's own records already are.
/// </param>
/// <param name="unitOfWork">The unit of work every staged change is committed through.</param>
/// <param name="eventPublisher">
///     The publisher this controller reports a change through once persisted, so
///     <c>AuthoritativeZoneCache</c> reloads it, <c>SignedZoneCache</c> re-signs it if applicable,
///     and any configured secondary is notified -- all three already wired to
///     <see cref="DnsZoneChangedEvent" /> already, with no new subscriber needed here.
/// </param>
/// <param name="catalogZoneMembershipService">
///     The service this controller's own catalog-membership actions delegate to -- see
///     <see cref="JoinCatalog" />/<see cref="LeaveCatalog" />/<see cref="ListCatalogMembers" />.
/// </param>
/// <param name="caaRecordReconciliationService">
///     The service <see cref="ReconcileCaa" />'s own on-demand action delegates directly to --
///     the very same instance <c>CaaRecordReconciliationEventSubscriber</c> uses off the event
///     bus, so an on-demand pass and an automatic one behave identically.
/// </param>
/// <param name="ptrSyncService">
///     The service <see cref="ReconcilePtr" />'s own on-demand action delegates directly to --
///     the very same instance <c>PtrSyncEventSubscriber</c> uses off the event bus.
/// </param>
[ApiController]
[Route("api/zones")]
public class ZonesController(
    IZoneRepository zoneRepository,
    IRecordRepository recordRepository,
    IDnssecKeyRepository dnssecKeyRepository,
    IUnitOfWork unitOfWork,
    IDnsServerEventPublisher eventPublisher,
    CatalogZoneMembershipService catalogZoneMembershipService,
    CaaRecordReconciliationService caaRecordReconciliationService,
    PtrSyncService ptrSyncService
) : ControllerBase
{
    /// <summary>
    ///     This action lists every zone this server hosts or references.
    /// </summary>
    [HttpGet]
    [RequirePermission("zones:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ZoneDetailPagedResultExample))]
    public async Task<ActionResult<PagedResult<ZoneDetail>>> List(
        [FromQuery] PaginationQuery pagination,
        CancellationToken stoppingToken
    )
    {
        (int skip, int take) = (pagination ?? new PaginationQuery()).Normalize();

        // Both the page itself and its total count are pushed down to the storage engine --
        // a real, translated ORDER BY/OFFSET/LIMIT (or the MongoDB equivalent) -- rather than
        // materializing every zone into memory first, so a large zone count never forces the
        // whole set into memory just to answer one page's worth of a request.
        IReadOnlyList<ZoneEntity> zones = await zoneRepository.SearchAsync(
            skip,
            take,
            stoppingToken
        );
        int totalCount = await zoneRepository.CountAsync(stoppingToken);

        return Ok(new PagedResult<ZoneDetail>([.. zones.Select(ToDetail)], skip, take, totalCount));
    }

    /// <summary>
    ///     This action looks up a single zone by its identifier.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("zones:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ZoneDetailExample))]
    public async Task<ActionResult<ZoneDetail>> Get(Guid id, CancellationToken stoppingToken)
    {
        ZoneEntity zone = await zoneRepository.GetByIdAsync(id, stoppingToken);
        if (zone is null)
            throw new NotFoundException("Zone", id.ToString());

        return Ok(ToDetail(zone));
    }

    /// <summary>
    ///     This action creates a new zone's metadata shell. The zone has no records -- not even
    ///     its own SOA -- until the caller adds them through <see cref="RecordsController" />.
    /// </summary>
    [HttpPost]
    [RequirePermission("zones:write")]
    [SwaggerRequestExample(typeof(ZoneWriteRequest), typeof(ZoneWriteRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status201Created, typeof(ZoneDetailExample))]
    public async Task<ActionResult<ZoneDetail>> Create(
        ZoneWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        if (!DnsName.TryParse(request?.Name, out DnsName _))
            throw new ValidationException($"'{request?.Name}' is not a valid DNS zone name.");

        if (await zoneRepository.FindByNameAsync(request.Name, stoppingToken) is not null)
            throw new ConflictException($"A zone named '{request.Name}' already exists.");

        ValidatePtrIpv4PrefixLength(request.PtrIpv4PrefixLength);
        ValidatePtrIpv6PrefixLength(request.PtrIpv6PrefixLength);

        ZoneEntity zone = new() { Id = Guid.NewGuid(), CreatedAtUtc = DateTimeOffset.UtcNow };
        ApplyWrite(zone, request);

        await zoneRepository.AddAsync(zone, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        // Harmless even though the zone has no records yet -- AuthoritativeZoneCache's own
        // reload simply caches an empty zone, ready the moment its first record (its own SOA)
        // is added through RecordsController.
        eventPublisher.Publish(new DnsZoneChangedEvent(DateTimeOffset.UtcNow, zone.Id, zone.Name));

        return CreatedAtAction(nameof(Get), new { id = zone.Id }, ToDetail(zone));
    }

    /// <summary>
    ///     This action fully replaces an existing zone's metadata.
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("zones:write")]
    [SwaggerRequestExample(typeof(ZoneWriteRequest), typeof(ZoneWriteRequestExample))]
    public async Task<IActionResult> Replace(
        Guid id,
        ZoneWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        ZoneEntity zone = await zoneRepository.GetByIdAsync(id, stoppingToken);
        if (zone is null)
            throw new NotFoundException("Zone", id.ToString());

        if (!DnsName.TryParse(request?.Name, out DnsName _))
            throw new ValidationException($"'{request?.Name}' is not a valid DNS zone name.");

        ZoneEntity existingByName = await zoneRepository.FindByNameAsync(
            request.Name,
            stoppingToken
        );
        if (existingByName is not null && existingByName.Id != id)
            throw new ConflictException($"A zone named '{request.Name}' already exists.");

        ValidatePtrIpv4PrefixLength(request.PtrIpv4PrefixLength);
        ValidatePtrIpv6PrefixLength(request.PtrIpv6PrefixLength);

        ApplyWrite(zone, request);
        zone.UpdatedAtUtc = DateTimeOffset.UtcNow;
        zoneRepository.Update(zone);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        eventPublisher.Publish(new DnsZoneChangedEvent(DateTimeOffset.UtcNow, zone.Id, zone.Name));

        return NoContent();
    }

    /// <summary>
    ///     This action partially updates an existing zone's metadata via RFC 7396 JSON Merge
    ///     Patch: a field absent from the patch document is left untouched, a field present with
    ///     a JSON <c>null</c> clears it (where the underlying field is itself nullable), and any
    ///     other present field overwrites it.
    /// </summary>
    [HttpPatch("{id:guid}")]
    [RequirePermission("zones:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(ZonePatchRequestExample))]
    public async Task<IActionResult> Patch(
        Guid id,
        [FromBody] JsonElement patch,
        CancellationToken stoppingToken
    )
    {
        ZoneEntity zone = await zoneRepository.GetByIdAsync(id, stoppingToken);
        if (zone is null)
            throw new NotFoundException("Zone", id.ToString());

        if (patch.TryGetMergePatchField("name", out JsonElement name))
        {
            string newName = name.GetString();
            if (!DnsName.TryParse(newName, out DnsName _))
                throw new ValidationException($"'{newName}' is not a valid DNS zone name.");

            ZoneEntity existingByName = await zoneRepository.FindByNameAsync(
                newName,
                stoppingToken
            );
            if (existingByName is not null && existingByName.Id != id)
                throw new ConflictException($"A zone named '{newName}' already exists.");

            zone.Name = newName;
        }

        if (patch.TryGetMergePatchField("type", out JsonElement type))
            zone.Type = JsonSerializer.Deserialize<ZoneTypeEnum>(type.GetRawText());

        if (patch.TryGetMergePatchField("isDnssecSigned", out JsonElement isDnssecSigned))
            zone.IsDnssecSigned = isDnssecSigned.GetBoolean();

        if (patch.TryGetMergePatchField("dnssecAlgorithm", out JsonElement dnssecAlgorithm))
            zone.DnssecAlgorithm =
                dnssecAlgorithm.ValueKind == JsonValueKind.Null
                    ? null
                    : JsonSerializer.Deserialize<DnssecAlgorithmEnum>(dnssecAlgorithm.GetRawText());

        if (
            patch.TryGetMergePatchField(
                "denialOfExistenceMode",
                out JsonElement denialOfExistenceMode
            )
        )
            zone.DenialOfExistenceMode =
                JsonSerializer.Deserialize<DnssecDenialOfExistenceModeEnum>(
                    denialOfExistenceMode.GetRawText()
                );

        if (patch.TryGetMergePatchField("nsec3Iterations", out JsonElement nsec3Iterations))
            zone.Nsec3Iterations = nsec3Iterations.GetUInt16();

        if (patch.TryGetMergePatchField("nsec3SaltHex", out JsonElement nsec3SaltHex))
            zone.Nsec3SaltHex =
                nsec3SaltHex.ValueKind == JsonValueKind.Null ? null : nsec3SaltHex.GetString();

        if (patch.TryGetMergePatchField("isDisabled", out JsonElement isDisabled))
            zone.IsDisabled = isDisabled.GetBoolean();

        if (
            patch.TryGetMergePatchField(
                "primaryServerAddresses",
                out JsonElement primaryServerAddresses
            )
        )
            zone.PrimaryServerAddresses =
            [
                .. primaryServerAddresses.EnumerateArray().Select(element => element.GetString()),
            ];

        if (patch.TryGetMergePatchField("forwarderAddresses", out JsonElement forwarderAddresses))
            zone.ForwarderAddresses =
            [
                .. forwarderAddresses.EnumerateArray().Select(element => element.GetString()),
            ];

        if (patch.TryGetMergePatchField("notifyAddresses", out JsonElement notifyAddresses))
            zone.NotifyAddresses =
            [
                .. notifyAddresses.EnumerateArray().Select(element => element.GetString()),
            ];

        if (patch.TryGetMergePatchField("tsigKeyName", out JsonElement tsigKeyName))
            zone.TsigKeyName =
                tsigKeyName.ValueKind == JsonValueKind.Null ? null : tsigKeyName.GetString();

        if (
            patch.TryGetMergePatchField(
                "dynamicUpdateEnabled",
                out JsonElement dynamicUpdateEnabled
            )
        )
            zone.DynamicUpdateEnabled = dynamicUpdateEnabled.GetBoolean();

        if (patch.TryGetMergePatchField("comments", out JsonElement comments))
            zone.Comments = comments.ValueKind == JsonValueKind.Null ? null : comments.GetString();

        if (
            patch.TryGetMergePatchField(
                "cloudProviderCredentialId",
                out JsonElement cloudProviderCredentialId
            )
        )
            zone.CloudProviderCredentialId =
                cloudProviderCredentialId.ValueKind == JsonValueKind.Null
                    ? null
                    : cloudProviderCredentialId.GetGuid();

        if (patch.TryGetMergePatchField("cloudProviderZoneId", out JsonElement cloudProviderZoneId))
            zone.CloudProviderZoneId =
                cloudProviderZoneId.ValueKind == JsonValueKind.Null
                    ? null
                    : cloudProviderZoneId.GetString();

        if (patch.TryGetMergePatchField("cloudSyncInterval", out JsonElement cloudSyncInterval))
            zone.CloudSyncInterval = JsonSerializer.Deserialize<TimeSpan>(
                cloudSyncInterval.GetRawText()
            );

        if (
            patch.TryGetMergePatchField(
                "autoManageCaaRecords",
                out JsonElement autoManageCaaRecords
            )
        )
            zone.AutoManageCaaRecords =
                autoManageCaaRecords.ValueKind == JsonValueKind.Null
                    ? null
                    : autoManageCaaRecords.GetBoolean();

        if (patch.TryGetMergePatchField("caaIssuerDomain", out JsonElement caaIssuerDomain))
            zone.CaaIssuerDomain =
                caaIssuerDomain.ValueKind == JsonValueKind.Null
                    ? null
                    : caaIssuerDomain.GetString();

        if (
            patch.TryGetMergePatchField(
                "autoManagePtrRecords",
                out JsonElement autoManagePtrRecords
            )
        )
            zone.AutoManagePtrRecords =
                autoManagePtrRecords.ValueKind == JsonValueKind.Null
                    ? null
                    : autoManagePtrRecords.GetBoolean();

        if (patch.TryGetMergePatchField("ptrIpv4PrefixLength", out JsonElement ptrIpv4PrefixLength))
        {
            int? value =
                ptrIpv4PrefixLength.ValueKind == JsonValueKind.Null
                    ? null
                    : ptrIpv4PrefixLength.GetInt32();
            ValidatePtrIpv4PrefixLength(value);
            zone.PtrIpv4PrefixLength = value;
        }

        if (patch.TryGetMergePatchField("ptrIpv6PrefixLength", out JsonElement ptrIpv6PrefixLength))
        {
            int? value =
                ptrIpv6PrefixLength.ValueKind == JsonValueKind.Null
                    ? null
                    : ptrIpv6PrefixLength.GetInt32();
            ValidatePtrIpv6PrefixLength(value);
            zone.PtrIpv6PrefixLength = value;
        }

        if (patch.TryGetMergePatchField("transferTransport", out JsonElement transferTransport))
            zone.TransferTransport = JsonSerializer.Deserialize<ZoneTransferTransportEnum>(
                transferTransport.GetRawText()
            );

        zone.UpdatedAtUtc = DateTimeOffset.UtcNow;
        zoneRepository.Update(zone);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        eventPublisher.Publish(new DnsZoneChangedEvent(DateTimeOffset.UtcNow, zone.Id, zone.Name));

        return NoContent();
    }

    /// <summary>
    ///     This action permanently deletes a zone, every record it contains, and every DNSSEC key
    ///     belonging to it.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("zones:write")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken stoppingToken)
    {
        ZoneEntity zone = await zoneRepository.GetByIdAsync(id, stoppingToken);
        if (zone is null)
            throw new NotFoundException("Zone", id.ToString());

        // Removed explicitly, rather than relying on the relational backend's own cascade
        // delete, since MongoDB has no equivalent foreign-key mechanism at all -- this way,
        // deleting a zone behaves identically regardless of which storage engine is active.
        IReadOnlyList<RecordEntity> records = await recordRepository.ListByZoneIdAsync(
            id,
            stoppingToken
        );
        foreach (RecordEntity record in records)
            recordRepository.Remove(record);

        IReadOnlyList<DnssecKeyEntity> dnssecKeys = await dnssecKeyRepository.ListByZoneIdAsync(
            id,
            stoppingToken
        );
        foreach (DnssecKeyEntity dnssecKey in dnssecKeys)
            dnssecKeyRepository.Remove(dnssecKey);

        zoneRepository.Remove(zone);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        // AuthoritativeZoneCache's own ReloadZoneAsync evicts a zone it can no longer find.
        eventPublisher.Publish(new DnsZoneChangedEvent(DateTimeOffset.UtcNow, zone.Id, zone.Name));

        return NoContent();
    }

    /// <summary>
    ///     This action lists the member zone names an RFC 9432 catalog zone (either the
    ///     <see cref="ZoneTypeEnum.Catalog" /> original or a <see cref="ZoneTypeEnum.SecondaryCatalog" />
    ///     copy of it) currently enumerates, parsed live from its own stored PTR records -- how an
    ///     operator actually sees a catalog's own current membership.
    /// </summary>
    [HttpGet("{id:guid}/catalog-members")]
    [RequirePermission("zones:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ZoneCatalogMembersResponseExample))]
    public async Task<ActionResult<ZoneCatalogMembersResponse>> ListCatalogMembers(
        Guid id,
        CancellationToken stoppingToken
    ) =>
        Ok(
            new ZoneCatalogMembersResponse(
                await catalogZoneMembershipService.ListMembersAsync(id, stoppingToken)
            )
        );

    /// <summary>
    ///     This action adds a <see cref="ZoneTypeEnum.Primary" />/<see cref="ZoneTypeEnum.CloudPrimary" />
    ///     zone this server originates as a member of a <see cref="ZoneTypeEnum.Catalog" /> zone
    ///     it also originates -- writing the member's own RFC 9432 PTR record into the catalog
    ///     zone's content and merging every currently healthy cluster peer's own address into
    ///     both zones' <c>NotifyAddresses</c>, so the change propagates cluster-wide with no
    ///     further, manually-maintained configuration. See <see cref="CatalogZoneMembershipService.JoinAsync" />
    ///     for the full mechanics.
    /// </summary>
    [HttpPost("{id:guid}/catalog-membership")]
    [RequirePermission("zones:write")]
    [SwaggerRequestExample(
        typeof(ZoneCatalogMembershipRequest),
        typeof(ZoneCatalogMembershipRequestExample)
    )]
    public async Task<IActionResult> JoinCatalog(
        Guid id,
        ZoneCatalogMembershipRequest request,
        CancellationToken stoppingToken
    )
    {
        await catalogZoneMembershipService.JoinAsync(
            id,
            request?.CatalogZoneId ?? Guid.Empty,
            stoppingToken
        );
        return NoContent();
    }

    /// <summary>
    ///     This action removes a zone's own membership in whichever catalog zone it currently
    ///     belongs to, deleting its member PTR record from that catalog zone's content. A no-op
    ///     if the zone is not currently a member of any catalog.
    /// </summary>
    [HttpDelete("{id:guid}/catalog-membership")]
    [RequirePermission("zones:write")]
    public async Task<IActionResult> LeaveCatalog(Guid id, CancellationToken stoppingToken)
    {
        await catalogZoneMembershipService.LeaveAsync(id, stoppingToken);
        return NoContent();
    }

    /// <summary>
    ///     This action triggers one real, synchronous CAA reconciliation pass for the zone right
    ///     now, rather than waiting for the next <c>DnsZoneChangedEvent</c> to trigger one
    ///     automatically -- useful right after changing <c>CaaAutoManagementOptions</c>'s own
    ///     global default, or simply to confirm a zone's CAA records are already up to date
    ///     without waiting on the event bus. Delegates directly to
    ///     <see cref="CaaRecordReconciliationService.ReconcileZoneAsync(Guid, CancellationToken)" />
    ///     -- the exact same reconciliation logic the automatic path already uses, so this action
    ///     can never behave differently from what would have happened anyway.
    /// </summary>
    [HttpPost("{id:guid}/reconcile-caa")]
    [RequirePermission("zones:write")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ZoneCaaReconciliationResponseExample))]
    public async Task<ActionResult<ZoneCaaReconciliationResponse>> ReconcileCaa(
        Guid id,
        CancellationToken stoppingToken
    )
    {
        if (await zoneRepository.GetByIdAsync(id, stoppingToken) is null)
            throw new NotFoundException("Zone", id.ToString());

        CaaReconciliationResult result = await caaRecordReconciliationService.ReconcileZoneAsync(
            id,
            stoppingToken
        );

        return Ok(
            new ZoneCaaReconciliationResponse(
                result.RecordsAdded,
                result.RecordsRemoved,
                result.Skipped,
                result.SkipReason
            )
        );
    }

    /// <summary>
    ///     This action triggers one real, synchronous PTR/reverse-zone sync reconciliation pass
    ///     for the zone right now, the same on-demand convenience <see cref="ReconcileCaa" />
    ///     offers for CAA reconciliation. Delegates directly to
    ///     <see cref="PtrSyncService.ReconcileZoneAsync" /> -- the exact same reconciliation logic
    ///     both the automatic (<c>DnsZoneChangedEvent</c>-triggered) path and this on-demand path
    ///     share, including RFC 2317 classless-delegation glue when this zone's own
    ///     <c>PtrIpv4PrefixLength</c> (or the global default) calls for it.
    /// </summary>
    [HttpPost("{id:guid}/reconcile-ptr")]
    [RequirePermission("zones:write")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ZonePtrReconciliationResponseExample))]
    public async Task<ActionResult<ZonePtrReconciliationResponse>> ReconcilePtr(
        Guid id,
        CancellationToken stoppingToken
    )
    {
        if (await zoneRepository.GetByIdAsync(id, stoppingToken) is null)
            throw new NotFoundException("Zone", id.ToString());

        PtrReconciliationResult result = await ptrSyncService.ReconcileZoneAsync(id, stoppingToken);

        return Ok(
            new ZonePtrReconciliationResponse(
                result.RecordsAdded,
                result.RecordsRemoved,
                result.ReverseZonesTouched,
                result.Skipped,
                result.SkipReason
            )
        );
    }

    /// <summary>
    ///     This method validates a PTR IPv4 reverse-zone grouping prefix length, throwing
    ///     <see cref="ValidationException" /> for anything other than <see langword="null" /> or a
    ///     value within <c>24</c>-<c>30</c> inclusive -- <c>24</c> is the conventional octet
    ///     boundary this feature always used before RFC 2317 support existed; <c>25</c>-<c>30</c>
    ///     is the classless range this project supports delegating at; a coarser value would span
    ///     more than one octet (unsupported), and <c>31</c>/<c>32</c> would leave a delegated block
    ///     too small to hold a usable host range.
    /// </summary>
    private static void ValidatePtrIpv4PrefixLength(int? prefixLength)
    {
        if (prefixLength is int value && (value < 24 || value > 30))
            throw new ValidationException(
                $"'{value}' is not a valid PTR IPv4 prefix length -- it must be between 24 and 30 inclusive."
            );
    }

    /// <summary>
    ///     This method validates a PTR IPv6 reverse-zone grouping prefix length, throwing
    ///     <see cref="ValidationException" /> for anything other than <see langword="null" /> or a
    ///     multiple of <c>4</c> between <c>16</c> and <c>124</c> inclusive -- every
    ///     <c>ip6.arpa</c> label is exactly one hex nibble (RFC 3596), so only a multiple-of-4 bit
    ///     count is even expressible as a whole number of labels.
    /// </summary>
    private static void ValidatePtrIpv6PrefixLength(int? prefixLength)
    {
        if (prefixLength is int value && (value % 4 != 0 || value < 16 || value > 124))
            throw new ValidationException(
                $"'{value}' is not a valid PTR IPv6 prefix length -- it must be a multiple of 4 between 16 and 124 inclusive."
            );
    }

    /// <summary>
    ///     This method copies every writable field from a write request onto a zone entity.
    /// </summary>
    private static void ApplyWrite(ZoneEntity zone, ZoneWriteRequest request)
    {
        zone.Name = request.Name;
        zone.Type = request.Type;
        zone.IsDnssecSigned = request.IsDnssecSigned;
        zone.DnssecAlgorithm = request.DnssecAlgorithm;
        zone.DenialOfExistenceMode = request.DenialOfExistenceMode;
        zone.Nsec3Iterations = request.Nsec3Iterations;
        zone.Nsec3SaltHex = request.Nsec3SaltHex;
        zone.IsDisabled = request.IsDisabled;
        zone.PrimaryServerAddresses = [.. request.PrimaryServerAddresses ?? []];
        zone.ForwarderAddresses = [.. request.ForwarderAddresses ?? []];
        zone.NotifyAddresses = [.. request.NotifyAddresses ?? []];
        zone.TsigKeyName = request.TsigKeyName;
        zone.DynamicUpdateEnabled = request.DynamicUpdateEnabled;
        zone.Comments = request.Comments;
        zone.CloudProviderCredentialId = request.CloudProviderCredentialId;
        zone.CloudProviderZoneId = request.CloudProviderZoneId;
        zone.CloudSyncInterval = request.CloudSyncInterval ?? TimeSpan.FromMinutes(5);
        zone.AutoManageCaaRecords = request.AutoManageCaaRecords;
        zone.CaaIssuerDomain = request.CaaIssuerDomain;
        zone.AutoManagePtrRecords = request.AutoManagePtrRecords;
        zone.PtrIpv4PrefixLength = request.PtrIpv4PrefixLength;
        zone.PtrIpv6PrefixLength = request.PtrIpv6PrefixLength;
        zone.TransferTransport = request.TransferTransport;
    }

    /// <summary>
    ///     This method converts a zone entity into its REST representation.
    /// </summary>
    private static ZoneDetail ToDetail(ZoneEntity zone) =>
        new(
            zone.Id,
            zone.Name,
            zone.Type,
            zone.IsDnssecSigned,
            zone.DnssecAlgorithm,
            zone.DenialOfExistenceMode,
            zone.Nsec3Iterations,
            zone.Nsec3SaltHex,
            zone.IsDisabled,
            zone.PrimaryServerAddresses,
            zone.ForwarderAddresses,
            zone.NotifyAddresses,
            zone.TsigKeyName,
            zone.DynamicUpdateEnabled,
            zone.Comments,
            zone.CloudProviderCredentialId,
            zone.CloudProviderZoneId,
            zone.CloudSyncInterval,
            zone.AutoManageCaaRecords,
            zone.CaaIssuerDomain,
            zone.AutoManagePtrRecords,
            zone.PtrIpv4PrefixLength,
            zone.PtrIpv6PrefixLength,
            zone.CreatedAtUtc,
            zone.UpdatedAtUtc,
            zone.TransferTransport,
            zone.MemberOfCatalogZoneId
        );
}
