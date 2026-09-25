using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Authoritative.Notify;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.CatalogSync;

/// <summary>
///     This class is the RFC 9432 (DNS Catalog Zones) consumer. When a
///     <see cref="ZoneTypeEnum.SecondaryCatalog" /> zone changes (after a transfer pull by
///     <see cref="Refresh.SecondaryZoneRefreshService" />), it compares the catalog's member list with
///     the zones already provisioned for it: it creates a <see cref="ZoneTypeEnum.Secondary" /> zone
///     for each new member and deletes members no longer listed.
///     <para>
///     A new member zone's <see cref="ZoneEntity.PrimaryServerAddresses" /> come from
///     <see cref="ClusterPeerAddressResolver" />, and it inherits the catalog zone's
///     <see cref="ZoneEntity.TsigKeyName" />, the key already used to pull the catalog, so no
///     per-member key has to be set up.
///     </para>
///     <para>
///     If a zone with a member's name already exists but its
///     <see cref="ZoneEntity.MemberOfCatalogZoneId" /> is not this catalog (an operator's zone, or a
///     member of another catalog), it is never changed or deleted.
///     </para>
/// </summary>
/// <param name="zoneRepository">The repository used to read the catalog zone and its provisioned members.</param>
/// <param name="recordRepository">The repository used to delete a removed member's records.</param>
/// <param name="unitOfWork">The unit of work every zone create/delete here is committed through.</param>
/// <param name="eventPublisher">
///     The publisher used to report a removed zone, so <c>AuthoritativeZoneCache</c> drops it, as
///     <c>ZonesController.Delete</c> does.
/// </param>
/// <param name="membershipService">The service used to read a catalog zone's member list.</param>
/// <param name="peerAddressResolver">The resolver used for a new member's primary server addresses.</param>
/// <param name="refreshTrigger">The scheduler told to pull a new member at once.</param>
/// <param name="logger">The logger provisioning decisions are recorded to.</param>
public class CatalogZoneAutoProvisioningService(
    IZoneRepository zoneRepository,
    IRecordRepository recordRepository,
    IUnitOfWork unitOfWork,
    IDnsServerEventPublisher eventPublisher,
    CatalogZoneMembershipService membershipService,
    ClusterPeerAddressResolver peerAddressResolver,
    ISecondaryZoneRefreshTrigger refreshTrigger,
    ILogger<CatalogZoneAutoProvisioningService> logger
)
{
    /// <summary>
    ///     This method reconciles one <see cref="ZoneTypeEnum.SecondaryCatalog" /> zone's member list
    ///     with the zones provisioned for it. <see cref="CatalogZoneAutoProvisioningEventSubscriber" />
    ///     calls it. It does nothing for other zone types, including a <see cref="ZoneTypeEnum.Catalog" />
    ///     zone, whose membership is managed through <see cref="CatalogZoneMembershipService" />.
    /// </summary>
    /// <param name="zoneId">The identifier of the changed zone.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public async Task ReconcileZoneAsync(Guid zoneId, CancellationToken stoppingToken = default)
    {
        ZoneEntity catalogZone = await zoneRepository.GetByIdAsync(zoneId, stoppingToken);

        if (catalogZone is null || catalogZone.Type != ZoneTypeEnum.SecondaryCatalog)
            return;

        IReadOnlyList<string> desiredMembers = await membershipService.ListMembersAsync(
            zoneId,
            stoppingToken
        );

        IReadOnlyList<ZoneEntity> alreadyProvisioned = await zoneRepository.ListAsync(
            zone => zone.MemberOfCatalogZoneId == zoneId,
            stoppingToken
        );

        HashSet<string> desiredSet = new(desiredMembers, StringComparer.OrdinalIgnoreCase);
        HashSet<string> provisionedSet = new(
            alreadyProvisioned.Select(zone => zone.Name),
            StringComparer.OrdinalIgnoreCase
        );

        List<string> toProvision =
        [
            .. desiredMembers.Where(name => !provisionedSet.Contains(name)),
        ];
        List<ZoneEntity> toDeprovision =
        [
            .. alreadyProvisioned.Where(zone => !desiredSet.Contains(zone.Name)),
        ];

        if (toProvision.Count == 0 && toDeprovision.Count == 0)
            return;

        if (toProvision.Count > 0)
        {
            IReadOnlyList<string> peerAddresses =
                await peerAddressResolver.ResolveHealthyPeerAddressesAsync(stoppingToken);

            foreach (string memberZoneName in toProvision)
                await ProvisionOneAsync(catalogZone, memberZoneName, peerAddresses, stoppingToken);
        }

        foreach (ZoneEntity staleZone in toDeprovision)
            await DeprovisionOneAsync(catalogZone, staleZone, stoppingToken);
    }

    /// <summary>
    ///     This method creates a new member as a <see cref="ZoneTypeEnum.Secondary" /> zone, or skips
    ///     it (with a log entry) when a zone of that name exists that this catalog does not own.
    /// </summary>
    private async Task ProvisionOneAsync(
        ZoneEntity catalogZone,
        string memberZoneName,
        IReadOnlyList<string> peerAddresses,
        CancellationToken stoppingToken
    )
    {
        ZoneEntity existingByName = await zoneRepository.FindByNameAsync(
            memberZoneName,
            stoppingToken
        );

        if (existingByName is not null)
        {
            logger.LogWarning(
                "Catalog zone '{CatalogZoneName}' lists member '{MemberZoneName}', but a zone "
                    + "with that name already exists locally and is not one this catalog "
                    + "provisioned; skipping auto-provisioning to avoid a conflicting duplicate.",
                catalogZone.Name,
                memberZoneName
            );
            return;
        }

        ZoneEntity newZone = new()
        {
            Id = Guid.NewGuid(),
            Name = memberZoneName,
            Type = ZoneTypeEnum.Secondary,
            MemberOfCatalogZoneId = catalogZone.Id,
            PrimaryServerAddresses = [.. peerAddresses],
            TsigKeyName = catalogZone.TsigKeyName,
            TransferTransport = catalogZone.TransferTransport,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        await zoneRepository.AddAsync(newZone, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        logger.LogInformation(
            "Auto-provisioned member zone '{MemberZoneName}' from catalog '{CatalogZoneName}', "
                + "with {PeerCount} known peer address(es) as its own primary servers.",
            memberZoneName,
            catalogZone.Name,
            peerAddresses.Count
        );

        // The new zone has no records yet, so pull it now, as after a NOTIFY.
        refreshTrigger.RequestImmediateRefresh(newZone.Id);
    }

    /// <summary>
    ///     This method deletes a member the catalog no longer lists: its records, then the zone, as
    ///     <c>ZonesController.Delete</c> does.
    /// </summary>
    private async Task DeprovisionOneAsync(
        ZoneEntity catalogZone,
        ZoneEntity staleZone,
        CancellationToken stoppingToken
    )
    {
        IReadOnlyList<RecordEntity> records = await recordRepository.ListByZoneIdAsync(
            staleZone.Id,
            stoppingToken
        );

        foreach (RecordEntity record in records)
            recordRepository.Remove(record);

        zoneRepository.Remove(staleZone);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        // AuthoritativeZoneCache.ReloadZoneAsync drops a zone it can no longer find.
        eventPublisher.Publish(
            new DnsZoneChangedEvent(DateTimeOffset.UtcNow, staleZone.Id, staleZone.Name)
        );

        logger.LogInformation(
            "Removed auto-provisioned member zone '{MemberZoneName}' -- catalog "
                + "'{CatalogZoneName}' no longer lists it.",
            staleZone.Name,
            catalogZone.Name
        );
    }
}
