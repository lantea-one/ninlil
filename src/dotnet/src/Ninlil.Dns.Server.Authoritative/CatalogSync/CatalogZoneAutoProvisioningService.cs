using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Authoritative.Notify;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.CatalogSync;

/// <summary>
///     This class is the actual RFC 9432 (DNS Catalog Zones) consumer-side auto-provisioning
///     engine: whenever a <see cref="ZoneTypeEnum.SecondaryCatalog" /> zone's own content changes
///     (via an ordinary AXFR/IXFR pull -- see <see cref="Refresh.SecondaryZoneRefreshService" />),
///     it diffs the catalog's own current member list against whatever this node has already
///     auto-provisioned on that catalog's behalf, creating a real, ordinary
///     <see cref="ZoneTypeEnum.Secondary" /> zone for a newly-listed member and removing a
///     previously-provisioned one the catalog no longer lists.
///     <para>
///         This is the "close the loop" piece connecting the cluster's own peer registry to real
///         zone propagation: a newly auto-provisioned zone's own
///         <see cref="ZoneEntity.PrimaryServerAddresses" /> is derived from
///         <see cref="ClusterPeerAddressResolver" />, not left blank for an operator to fill in,
///         and it inherits the catalog zone's own <see cref="ZoneEntity.TsigKeyName" /> -- the
///         one trust relationship this node already had to have provisioned to pull the catalog
///         zone itself, reused rather than requiring a second, per-member-zone key an operator
///         would otherwise have to hand-provision on both sides for every single member.
///     </para>
///     <para>
///         Deliberately conservative about a name collision: if a locally-provisioned zone
///         already exists with the exact name a catalog wants to add, but that zone's own
///         <see cref="ZoneEntity.MemberOfCatalogZoneId" /> does not match this catalog (an
///         operator's own, unrelated zone, or one this same node already auto-provisioned on
///         behalf of a *different* catalog), this service never overwrites or deletes it --
///         only ever touching a zone whose own field already marks it as this catalog's.
///     </para>
/// </summary>
/// <param name="zoneRepository">The repository used to read the catalog zone and its own currently-provisioned members.</param>
/// <param name="recordRepository">The repository used to remove a de-provisioned zone's own records.</param>
/// <param name="unitOfWork">The unit of work every zone create/delete here is committed through.</param>
/// <param name="eventPublisher">
///     The publisher a removed zone's own change is reported through, so
///     <c>AuthoritativeZoneCache</c> evicts it -- mirroring <c>ZonesController.Delete</c>'s own
///     final step exactly.
/// </param>
/// <param name="membershipService">The service this class reuses to parse a catalog zone's own current member list.</param>
/// <param name="peerAddressResolver">The resolver used to derive a newly-provisioned zone's own primary server addresses.</param>
/// <param name="refreshTrigger">The scheduler told to pull a newly-provisioned zone's own content immediately.</param>
/// <param name="logger">The logger this service records provisioning/de-provisioning decisions to.</param>
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
    ///     This method reconciles a single <see cref="ZoneTypeEnum.SecondaryCatalog" /> zone's own
    ///     current member list against what this node has already auto-provisioned for it -- the
    ///     entry point <see cref="CatalogZoneAutoProvisioningEventSubscriber" /> uses in response
    ///     to a <see cref="DnsZoneChangedEvent" />. A no-op for any other zone type, including the
    ///     <see cref="ZoneTypeEnum.Catalog" /> original itself -- that side's own membership is
    ///     managed directly, by an operator, through <see cref="CatalogZoneMembershipService" />.
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
    ///     This method auto-provisions a single newly-listed member zone as an ordinary
    ///     <see cref="ZoneTypeEnum.Secondary" /> zone, skipping it (log-only) rather than creating
    ///     a conflicting duplicate if a same-named zone this catalog does not itself own already
    ///     exists locally.
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

        // The freshly-created zone has no records of its own yet -- ask
        // SecondaryZoneRefreshService to pull it right away rather than waiting for its own next
        // scheduled poll, the same immediacy an inbound NOTIFY already gets.
        refreshTrigger.RequestImmediateRefresh(newZone.Id);
    }

    /// <summary>
    ///     This method removes a single previously-auto-provisioned member zone the catalog no
    ///     longer lists -- its own records, then the zone itself, exactly mirroring
    ///     <c>ZonesController.Delete</c>'s own cleanup.
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

        // AuthoritativeZoneCache's own ReloadZoneAsync evicts a zone it can no longer find, the
        // same as any other zone deletion.
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
