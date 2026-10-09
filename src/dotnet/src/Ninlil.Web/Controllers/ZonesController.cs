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
using Ninlil.Dns.Server.Dnssec.Algorithms;
using Ninlil.Web.Examples.Zones;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller manages the DNS zones this server hosts or refers to: creating, updating
///     and deleting a zone's settings (name, type, DNSSEC, transfer, forwarding, cloud sync, CAA
///     and PTR management), catalog-zone membership, and on-demand CAA and PTR reconciliation. A
///     zone's records are managed through <see cref="RecordsController" />. Reads need
///     <c>zones:read</c> and changes need <c>zones:write</c>. Every change publishes a
///     <see cref="DnsZoneChangedEvent" />.
/// </summary>
/// <param name="zoneRepository">Reads and writes zones.</param>
/// <param name="recordRepository">Removes a deleted zone's records.</param>
/// <param name="dnssecKeyRepository">
///     Removes a deleted zone's DNSSEC keys (see <see cref="DnssecKeysController" />).
///     <see cref="Ninlil.Dns.Data.Abstractions.Entity.DnssecKeyEntity" /> refers to its zone by
///     <c>ZoneId</c> only, and MongoDB has no cascade delete, so the keys are removed explicitly,
///     like the records.
/// </param>
/// <param name="tsigKeyRepository">
///     Checks that a zone's <c>TsigKeyName</c> does not name a cluster key.
/// </param>
/// <param name="unitOfWork">Commits staged changes.</param>
/// <param name="eventPublisher">
///     Publishes <see cref="DnsZoneChangedEvent" /> after a change is saved, so
///     <c>AuthoritativeZoneCache</c> reloads the zone, <c>SignedZoneCache</c> re-signs it if
///     needed, and configured secondaries are notified.
/// </param>
/// <param name="catalogZoneMembershipService">
///     Handles <see cref="JoinCatalog" />, <see cref="LeaveCatalog" /> and
///     <see cref="ListCatalogMembers" />.
/// </param>
/// <param name="caaRecordReconciliationService">
///     Runs <see cref="ReconcileCaa" />; it is the same instance
///     <c>CaaRecordReconciliationEventSubscriber</c> uses, so on-demand and automatic passes
///     behave the same.
/// </param>
/// <param name="ptrSyncService">
///     Runs <see cref="ReconcilePtr" />; it is the same instance <c>PtrSyncEventSubscriber</c>
///     uses.
/// </param>
[ApiController]
[Route("api/zones")]
public class ZonesController(
    IZoneRepository zoneRepository,
    IRecordRepository recordRepository,
    IDnssecKeyRepository dnssecKeyRepository,
    ITsigKeyRepository tsigKeyRepository,
    IUnitOfWork unitOfWork,
    IDnsServerEventPublisher eventPublisher,
    CatalogZoneMembershipService catalogZoneMembershipService,
    CaaRecordReconciliationService caaRecordReconciliationService,
    PtrSyncService ptrSyncService
) : ControllerBase
{
    /// <summary>
    ///     This action lists zones, one page at a time. Requires <c>zones:read</c>.
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

        // The page and the total count are both computed by the storage engine (ORDER
        // BY/OFFSET/LIMIT or the MongoDB equivalent), not by loading every zone into memory.
        IReadOnlyList<ZoneEntity> zones = await zoneRepository.SearchAsync(
            skip,
            take,
            stoppingToken
        );
        int totalCount = await zoneRepository.CountAsync(stoppingToken);

        return Ok(new PagedResult<ZoneDetail>([.. zones.Select(ToDetail)], skip, take, totalCount));
    }

    /// <summary>
    ///     This action returns one zone's settings. Returns 404 for an unknown id. Requires
    ///     <c>zones:read</c>.
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
    ///     This action creates a zone with no records, not even an SOA; add them through
    ///     <see cref="RecordsController" />. Returns 400 for an invalid zone name, a PTR prefix
    ///     length or a TSIG key name that names a cluster key, and 409 when the name is taken. Requires <c>zones:write</c>.
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

        ValidateDnssecAlgorithm(request.DnssecAlgorithm);
        ValidatePtrIpv4PrefixLength(request.PtrIpv4PrefixLength);
        ValidatePtrIpv6PrefixLength(request.PtrIpv6PrefixLength);
        await ValidateTsigKeyNameAsync(request.TsigKeyName, stoppingToken);
        List<string> updateTsigKeyNames = await NormalizeBoundKeyNamesAsync(
            request.UpdateTsigKeyNames,
            "updateTsigKeyNames",
            stoppingToken
        );
        List<string> transferTsigKeyNames = await NormalizeBoundKeyNamesAsync(
            request.TransferTsigKeyNames,
            "transferTsigKeyNames",
            stoppingToken
        );

        ZoneEntity zone = new() { Id = Guid.NewGuid(), CreatedAtUtc = DateTimeOffset.UtcNow };
        ApplyWrite(zone, request, updateTsigKeyNames, transferTsigKeyNames);

        await zoneRepository.AddAsync(zone, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        // AuthoritativeZoneCache simply caches the empty zone, ready for its first record (the SOA)
        // to be added through RecordsController.
        eventPublisher.Publish(new DnsZoneChangedEvent(DateTimeOffset.UtcNow, zone.Id, zone.Name));

        return CreatedAtAction(nameof(Get), new { id = zone.Id }, ToDetail(zone));
    }

    /// <summary>
    ///     This action replaces a zone's settings. Validation and errors are as for
    ///     <see cref="Create" />, plus 404 for an unknown id. Requires <c>zones:write</c>.
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

        ValidateDnssecAlgorithm(request.DnssecAlgorithm);
        ValidatePtrIpv4PrefixLength(request.PtrIpv4PrefixLength);
        ValidatePtrIpv6PrefixLength(request.PtrIpv6PrefixLength);
        await ValidateTsigKeyNameAsync(request.TsigKeyName, stoppingToken);
        List<string> updateTsigKeyNames = await NormalizeBoundKeyNamesAsync(
            request.UpdateTsigKeyNames,
            "updateTsigKeyNames",
            stoppingToken
        );
        List<string> transferTsigKeyNames = await NormalizeBoundKeyNamesAsync(
            request.TransferTsigKeyNames,
            "transferTsigKeyNames",
            stoppingToken
        );

        ApplyWrite(zone, request, updateTsigKeyNames, transferTsigKeyNames);
        zone.UpdatedAtUtc = DateTimeOffset.UtcNow;
        zoneRepository.Update(zone);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        eventPublisher.Publish(new DnsZoneChangedEvent(DateTimeOffset.UtcNow, zone.Id, zone.Name));

        return NoContent();
    }

    /// <summary>
    ///     This action updates a zone's settings as an RFC 7396 merge patch: an absent field is
    ///     unchanged, a JSON <c>null</c> clears a nullable field, and any other value replaces it.
    ///     Returns 400 for an invalid name or PTR prefix length, 404 for an unknown id and 409 when
    ///     a new name is taken. Requires <c>zones:write</c>.
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
        {
            zone.DnssecAlgorithm =
                dnssecAlgorithm.ValueKind == JsonValueKind.Null
                    ? null
                    : JsonSerializer.Deserialize<DnssecAlgorithmEnum>(dnssecAlgorithm.GetRawText());
            ValidateDnssecAlgorithm(zone.DnssecAlgorithm);
        }

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
        {
            string newTsigKeyName =
                tsigKeyName.ValueKind == JsonValueKind.Null ? null : tsigKeyName.GetString();
            await ValidateTsigKeyNameAsync(newTsigKeyName, stoppingToken);
            zone.TsigKeyName = newTsigKeyName;
        }

        if (
            patch.TryGetMergePatchField(
                "dynamicUpdateEnabled",
                out JsonElement dynamicUpdateEnabled
            )
        )
            zone.DynamicUpdateEnabled = dynamicUpdateEnabled.GetBoolean();

        if (patch.TryGetMergePatchField("validateDnssec", out JsonElement validateDnssec))
            zone.ValidateDnssec = validateDnssec.GetBoolean();

        if (patch.TryGetMergePatchField("updateTsigKeyNames", out JsonElement updateTsigKeyNames))
            zone.UpdateTsigKeyNames = await NormalizeBoundKeyNamesAsync(
                ReadStringList(updateTsigKeyNames),
                "updateTsigKeyNames",
                stoppingToken
            );

        if (
            patch.TryGetMergePatchField(
                "transferTsigKeyNames",
                out JsonElement transferTsigKeyNames
            )
        )
            zone.TransferTsigKeyNames = await NormalizeBoundKeyNamesAsync(
                ReadStringList(transferTsigKeyNames),
                "transferTsigKeyNames",
                stoppingToken
            );

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
    ///     This action deletes a zone with all its records and DNSSEC keys. Returns 404 for an
    ///     unknown id. Requires <c>zones:write</c>.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("zones:write")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken stoppingToken)
    {
        ZoneEntity zone = await zoneRepository.GetByIdAsync(id, stoppingToken);
        if (zone is null)
            throw new NotFoundException("Zone", id.ToString());

        // Removed explicitly rather than by a relational cascade delete, which MongoDB lacks, so a
        // delete behaves the same on every storage engine.
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

        // AuthoritativeZoneCache.ReloadZoneAsync drops a zone it can no longer find.
        eventPublisher.Publish(new DnsZoneChangedEvent(DateTimeOffset.UtcNow, zone.Id, zone.Name));

        return NoContent();
    }

    /// <summary>
    ///     This action lists the member zone names of an RFC 9432 catalog zone (a
    ///     <see cref="ZoneTypeEnum.Catalog" /> zone or a
    ///     <see cref="ZoneTypeEnum.SecondaryCatalog" /> copy), read from its stored PTR records.
    ///     Requires <c>zones:read</c>.
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
    ///     This action makes a <see cref="ZoneTypeEnum.Primary" /> or
    ///     <see cref="ZoneTypeEnum.CloudPrimary" /> zone that this server originates a member of a
    ///     <see cref="ZoneTypeEnum.Catalog" /> zone it also originates. It writes the member's RFC
    ///     9432 PTR record into the catalog zone and adds every healthy cluster peer's address to
    ///     both zones' <c>NotifyAddresses</c>, so the change reaches the cluster without manual
    ///     configuration (see <see cref="CatalogZoneMembershipService.JoinAsync" />). Requires
    ///     <c>zones:write</c>.
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
    ///     This action removes a zone from the catalog zone it belongs to, deleting its member PTR
    ///     record there. It does nothing if the zone belongs to no catalog. Requires
    ///     <c>zones:write</c>.
    /// </summary>
    [HttpDelete("{id:guid}/catalog-membership")]
    [RequirePermission("zones:write")]
    public async Task<IActionResult> LeaveCatalog(Guid id, CancellationToken stoppingToken)
    {
        await catalogZoneMembershipService.LeaveAsync(id, stoppingToken);
        return NoContent();
    }

    /// <summary>
    ///     This action runs one CAA reconciliation pass for the zone now and returns how many
    ///     records were added or removed, or why the pass was skipped. The same pass runs
    ///     automatically on each <c>DnsZoneChangedEvent</c>; running it by hand is useful after
    ///     changing the server-wide CAA default. It calls
    ///     <see cref="CaaRecordReconciliationService.ReconcileZoneAsync(Guid, CancellationToken)" />,
    ///     like the automatic path. Returns 404 for an unknown id. Requires <c>zones:write</c>.
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
    ///     This action runs one PTR (reverse-zone) sync pass for the zone now, through
    ///     <see cref="PtrSyncService.ReconcileZoneAsync" />, the same logic the automatic
    ///     <c>DnsZoneChangedEvent</c> path uses, including RFC 2317 classless-delegation records
    ///     when the zone's <c>PtrIpv4PrefixLength</c> (or the server-wide default) calls for them.
    ///     Returns the records added and removed and the reverse zones touched, or why the pass was
    ///     skipped, and 404 for an unknown id. Requires <c>zones:write</c>.
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
    ///     This method rejects a zone signing algorithm this server never signs with
    ///     (<see cref="DnssecAlgorithmRegistry.IsSigningProhibited" />). Null passes.
    /// </summary>
    /// <exception cref="ValidationException">The algorithm is prohibited for signing.</exception>
    private static void ValidateDnssecAlgorithm(DnssecAlgorithmEnum? algorithm)
    {
        if (algorithm is { } value && DnssecAlgorithmRegistry.IsSigningProhibited(value))
            throw new ValidationException(
                $"DNSSEC algorithm '{value}' is prohibited for signing (RFC 8624 §3.1)."
            );
    }

    /// <summary>
    ///     This method checks a PTR IPv4 reverse-zone prefix length: <see langword="null" /> or 24
    ///     to 30 inclusive. 24 is the octet boundary; 25 to 30 are the classless (RFC 2317) lengths
    ///     that can be delegated. A shorter prefix would span more than one octet, which is not
    ///     supported, and 31 or 32 leaves no usable host range.
    /// </summary>
    private static void ValidatePtrIpv4PrefixLength(int? prefixLength)
    {
        if (prefixLength is int value && (value < 24 || value > 30))
            throw new ValidationException(
                $"'{value}' is not a valid PTR IPv4 prefix length -- it must be between 24 and 30 inclusive."
            );
    }

    /// <summary>
    ///     This method checks a PTR IPv6 reverse-zone prefix length: <see langword="null" /> or a
    ///     multiple of 4 from 16 to 124 inclusive. Each <c>ip6.arpa</c> label is one hex nibble
    ///     (RFC 3596), so only multiples of 4 map to whole labels.
    /// </summary>
    private static void ValidatePtrIpv6PrefixLength(int? prefixLength)
    {
        if (prefixLength is int value && (value % 4 != 0 || value < 16 || value > 124))
            throw new ValidationException(
                $"'{value}' is not a valid PTR IPv6 prefix length -- it must be a multiple of 4 between 16 and 124 inclusive."
            );
    }

    /// <summary>
    ///     This method rejects a TSIG key name that names a cluster key. A key clustering created
    ///     never signs or verifies zone transfers or updates (<c>TsigService</c> treats it as
    ///     unknown), so a zone pointing at one could never transfer. A name that matches no key
    ///     is allowed, because a zone may be set up before its key.
    /// </summary>
    /// <exception cref="ValidationException">The name is a cluster key's.</exception>
    private async Task ValidateTsigKeyNameAsync(string tsigKeyName, CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(tsigKeyName))
            return;

        // Key names are stored in lowercase, so look that spelling up first (the lookup
        // TsigService makes), then the text as given for a row stored before the rule.
        string exact = tsigKeyName.TrimEnd('.');
        TsigKeyEntity key =
            await tsigKeyRepository.FindByNameAsync(exact.ToLowerInvariant(), stoppingToken)
            ?? await tsigKeyRepository.FindByNameAsync(exact, stoppingToken);

        if (key is { ClusterNodeId: not null } or { ClusterInviteExpiresAtUtc: not null })
            throw new ValidationException(
                $"'{tsigKeyName}' is a cluster key, managed by clustering; choose a TSIG key created for zone transfers."
            );
    }

    /// <summary>
    ///     This method reads a merge-patch JSON array of strings; <c>null</c> reads as an empty
    ///     list.
    /// </summary>
    private static List<string> ReadStringList(JsonElement element) =>
        element.ValueKind == JsonValueKind.Null
            ? []
            : [.. element.EnumerateArray().Select(item => item.GetString())];

    /// <summary>
    ///     This method validates and canonicalizes a zone's list of bound TSIG key names
    ///     (<c>UpdateTsigKeyNames</c> or <c>TransferTsigKeyNames</c>). Each name must parse as a DNS
    ///     name and must not name a cluster key (same rule as <c>TsigKeyName</c>, which
    ///     <see cref="ValidateTsigKeyNameAsync" /> applies). A name that matches no key is
    ///     accepted, because a zone may be set up before its key. Names are stored lowercase
    ///     without a trailing dot, as TSIG key names are, and duplicates are dropped.
    /// </summary>
    /// <exception cref="ValidationException">A name is invalid or is a cluster key's.</exception>
    private async Task<List<string>> NormalizeBoundKeyNamesAsync(
        IReadOnlyList<string> keyNames,
        string fieldName,
        CancellationToken stoppingToken
    )
    {
        List<string> normalized = [];

        foreach (string keyName in keyNames ?? [])
        {
            if (
                !DnsName.TryParse(keyName, out DnsName parsed) || string.IsNullOrWhiteSpace(keyName)
            )
                throw new ValidationException(
                    $"'{keyName}' in {fieldName} is not a valid TSIG key name."
                );

            string canonical = parsed.ToCanonicalLower().ToString().TrimEnd('.');
            await ValidateTsigKeyNameAsync(canonical, stoppingToken);

            if (!normalized.Contains(canonical))
                normalized.Add(canonical);
        }

        return normalized;
    }

    /// <summary>
    ///     This method copies every writable field from a write request onto a zone entity. A
    ///     missing <c>CloudSyncInterval</c> defaults to five minutes.
    /// </summary>
    private static void ApplyWrite(
        ZoneEntity zone,
        ZoneWriteRequest request,
        List<string> updateTsigKeyNames,
        List<string> transferTsigKeyNames
    )
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
        zone.UpdateTsigKeyNames = updateTsigKeyNames;
        zone.TransferTsigKeyNames = transferTsigKeyNames;
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
        zone.ValidateDnssec = request.ValidateDnssec;
    }

    /// <summary>
    ///     This method converts a zone entity to its API representation.
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
            zone.UpdateTsigKeyNames,
            zone.TransferTsigKeyNames,
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
            zone.MemberOfCatalogZoneId,
            zone.ValidateDnssec
        );
}
