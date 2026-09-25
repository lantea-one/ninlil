using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Authoritative.Journal;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.CatalogSync;

/// <summary>
///     This class is the RFC 9432 (DNS Catalog Zones) producer: it adds a
///     <see cref="ZoneTypeEnum.Primary" /> or <see cref="ZoneTypeEnum.CloudPrimary" /> zone to, or
///     removes it from, a <see cref="ZoneTypeEnum.Catalog" /> zone on this server, writing the
///     catalog records (<see cref="CatalogZoneRecordNames" />) and adding cluster peer addresses to
///     both zones' <see cref="ZoneEntity.NotifyAddresses" />. Record writes go through
///     <see cref="IZoneUpdateService" />, so the catalog reaches a
///     <see cref="ZoneTypeEnum.SecondaryCatalog" /> peer by the usual NOTIFY and AXFR/IXFR with TSIG.
///     <para>
///     Only the RFC 9432 §5 member PTR (<c>&lt;unique-N&gt;.zones.$CATZ</c>) and the §3
///     <c>version.$CATZ</c> record are produced; the <c>coo</c> and <c>group</c> properties are not
///     supported.
///     </para>
/// </summary>
/// <param name="zoneRepository">The repository used to look up the catalog and member zones.</param>
/// <param name="recordRepository">The repository used to find a catalog zone's existing records.</param>
/// <param name="zoneUpdateService">The service every catalog record write goes through.</param>
/// <param name="unitOfWork">The unit of work zone fields (catalog membership, NotifyAddresses) are saved through.</param>
/// <param name="peerAddressResolver">The resolver for the addresses of healthy cluster peers.</param>
public class CatalogZoneMembershipService(
    IZoneRepository zoneRepository,
    IRecordRepository recordRepository,
    IZoneUpdateService zoneUpdateService,
    IUnitOfWork unitOfWork,
    ClusterPeerAddressResolver peerAddressResolver
)
{
    /// <summary>
    ///     This constant contains the TTL of catalog records: 0, since RFC 9432 §3 says the TTL has
    ///     no meaning and consumers should ignore it.
    /// </summary>
    private const uint CatalogRecordTimeToLiveSeconds = 0;

    /// <summary>
    ///     This method reports whether a zone type may join a catalog: <see cref="ZoneTypeEnum.Primary" />
    ///     or <see cref="ZoneTypeEnum.CloudPrimary" />, the types this server is the source for, as in
    ///     <c>PtrSyncService.IsEligibleZoneType</c>.
    /// </summary>
    public static bool IsEligibleMemberZoneType(ZoneTypeEnum zoneType) =>
        zoneType == ZoneTypeEnum.Primary || zoneType == ZoneTypeEnum.CloudPrimary;

    /// <summary>
    ///     This method lists the member zone names in a <see cref="ZoneTypeEnum.Catalog" /> or
    ///     <see cref="ZoneTypeEnum.SecondaryCatalog" /> zone, parsed from its stored PTR records. The REST
    ///     API and the zone page use it.
    /// </summary>
    /// <param name="catalogZoneId">The identifier of the catalog (or secondary-catalog) zone to list.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The member zone names, in alphabetical order.</returns>
    public async Task<IReadOnlyList<string>> ListMembersAsync(
        Guid catalogZoneId,
        CancellationToken stoppingToken = default
    )
    {
        ZoneEntity catalogZone =
            await zoneRepository.GetByIdAsync(catalogZoneId, stoppingToken)
            ?? throw new NotFoundException("Zone", catalogZoneId.ToString());

        if (catalogZone.Type is not (ZoneTypeEnum.Catalog or ZoneTypeEnum.SecondaryCatalog))
            throw new ValidationException(
                $"Zone '{catalogZone.Name}' is not a catalog zone -- its own type is '{catalogZone.Type}'."
            );

        IReadOnlyList<RecordEntity> records = await recordRepository.ListByZoneIdAsync(
            catalogZoneId,
            stoppingToken
        );

        return
        [
            .. records
                .Select(record =>
                    CatalogZoneRecordNames.TryGetMemberZoneName(
                        record,
                        catalogZone.Name,
                        out string memberZoneName
                    )
                        ? memberZoneName
                        : null
                )
                .Where(name => name is not null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase),
        ];
    }

    /// <summary>
    ///     This method adds a zone to a catalog on this server: it writes the RFC 9432 records
    ///     (creating the version record with the first member) and adds healthy cluster peers to both
    ///     zones' <see cref="ZoneEntity.NotifyAddresses" />, so later changes to either reach every peer.
    ///     Adding a zone that is already a member of the same catalog does nothing.
    /// </summary>
    /// <param name="memberZoneId">The identifier of the zone joining the catalog.</param>
    /// <param name="catalogZoneId">The identifier of the catalog zone to join.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public async Task JoinAsync(
        Guid memberZoneId,
        Guid catalogZoneId,
        CancellationToken stoppingToken = default
    )
    {
        ZoneEntity memberZone =
            await zoneRepository.GetByIdAsync(memberZoneId, stoppingToken)
            ?? throw new NotFoundException("Zone", memberZoneId.ToString());
        ZoneEntity catalogZone =
            await zoneRepository.GetByIdAsync(catalogZoneId, stoppingToken)
            ?? throw new NotFoundException("Zone", catalogZoneId.ToString());

        if (catalogZone.Type != ZoneTypeEnum.Catalog)
            throw new ValidationException(
                $"Zone '{catalogZone.Name}' is not a Catalog zone this server originates -- "
                    + "only a locally-authored catalog zone (ZoneTypeEnum.Catalog) can have "
                    + "members added to it here."
            );

        if (!IsEligibleMemberZoneType(memberZone.Type))
            throw new ValidationException(
                $"Zone '{memberZone.Name}' cannot join a catalog -- only a Primary or "
                    + "CloudPrimary zone this server owns the content of can."
            );

        if (memberZoneId == catalogZoneId)
            throw new ValidationException("A zone cannot be a member of itself.");

        if (memberZone.MemberOfCatalogZoneId == catalogZoneId)
            return;

        if (memberZone.MemberOfCatalogZoneId is { } existingCatalogZoneId)
            throw new ConflictException(
                $"Zone '{memberZone.Name}' already belongs to a different catalog zone "
                    + $"('{existingCatalogZoneId}'); remove it from that one first."
            );

        // Save the address merge before changing the catalog content: ApplyChangesAsync publishes
        // DnsZoneChangedEvent, and ZoneNotifyEventSubscriber then reads the zone in the background,
        // so the first NOTIFY must already see the merged addresses.
        IReadOnlyList<string> peerAddresses =
            await peerAddressResolver.ResolveHealthyPeerAddressesAsync(stoppingToken);

        bool catalogAddressesChanged = MergeNotifyAddresses(catalogZone, peerAddresses);
        bool memberAddressesChanged = MergeNotifyAddresses(memberZone, peerAddresses);

        memberZone.MemberOfCatalogZoneId = catalogZoneId;
        memberZone.UpdatedAtUtc = DateTimeOffset.UtcNow;
        zoneRepository.Update(memberZone);

        if (catalogAddressesChanged)
        {
            catalogZone.UpdatedAtUtc = DateTimeOffset.UtcNow;
            zoneRepository.Update(catalogZone);
        }

        if (memberAddressesChanged)
            zoneRepository.Update(memberZone);

        await unitOfWork.SaveChangesAsync(stoppingToken);

        string uniqueLabel = CatalogZoneRecordNames.ComputeUniqueLabel(memberZone.Name);
        List<RecordEntity> recordsToAdd =
        [
            BuildMemberPtrRecord(catalogZone, memberZone.Name, uniqueLabel),
        ];

        await EnsureVersionRecordAsync(catalogZone, recordsToAdd, stoppingToken);

        try
        {
            await zoneUpdateService.ApplyChangesAsync(
                catalogZone.Id,
                recordsToAdd,
                [],
                null,
                stoppingToken
            );
        }
        catch (InvalidOperationException)
        {
            throw new ValidationException(
                $"Catalog zone '{catalogZone.Name}' has no SOA record yet -- create one (through "
                    + "the ordinary zone-records endpoints, exactly like any other zone) before "
                    + "adding a member to it."
            );
        }
    }

    /// <summary>
    ///     This method removes a zone from its catalog and deletes its member PTR record. For a zone
    ///     in no catalog it does nothing.
    /// </summary>
    /// <param name="memberZoneId">The identifier of the zone leaving its catalog.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public async Task LeaveAsync(Guid memberZoneId, CancellationToken stoppingToken = default)
    {
        ZoneEntity memberZone =
            await zoneRepository.GetByIdAsync(memberZoneId, stoppingToken)
            ?? throw new NotFoundException("Zone", memberZoneId.ToString());

        if (memberZone.MemberOfCatalogZoneId is not { } catalogZoneId)
            return;

        ZoneEntity catalogZone = await zoneRepository.GetByIdAsync(catalogZoneId, stoppingToken);

        memberZone.MemberOfCatalogZoneId = null;
        memberZone.UpdatedAtUtc = DateTimeOffset.UtcNow;
        zoneRepository.Update(memberZone);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        // The catalog zone may already be deleted (members need not be removed first); then
        // clearing the member's field is all there is to do.
        if (catalogZone is null)
            return;

        string uniqueLabel = CatalogZoneRecordNames.ComputeUniqueLabel(memberZone.Name);
        string ownerName = CatalogZoneRecordNames.MemberRecordOwnerName(
            catalogZone.Name,
            uniqueLabel
        );

        IReadOnlyList<RecordEntity> existingMemberRecords = await recordRepository.FindByNameAsync(
            catalogZone.Id,
            ownerName,
            DnsResourceRecordTypeEnum.Ptr,
            stoppingToken
        );

        if (existingMemberRecords.Count == 0)
            return;

        try
        {
            await zoneUpdateService.ApplyChangesAsync(
                catalogZone.Id,
                [],
                [.. existingMemberRecords],
                null,
                stoppingToken
            );
        }
        catch (InvalidOperationException)
        {
            // The catalog zone has no SOA, so it cannot be changed; this should not happen for a zone
            // that gained a member.
        }
    }

    /// <summary>
    ///     This method adds the RFC 9432 §3 version TXT record to <paramref name="recordsToAdd" /> if
    ///     the catalog does not have it yet.
    /// </summary>
    private async Task EnsureVersionRecordAsync(
        ZoneEntity catalogZone,
        List<RecordEntity> recordsToAdd,
        CancellationToken stoppingToken
    )
    {
        string versionOwnerName = CatalogZoneRecordNames.VersionRecordOwnerName(catalogZone.Name);

        IReadOnlyList<RecordEntity> existing = await recordRepository.FindByNameAsync(
            catalogZone.Id,
            versionOwnerName,
            DnsResourceRecordTypeEnum.Txt,
            stoppingToken
        );

        if (existing.Count > 0)
            return;

        recordsToAdd.Add(
            new RecordEntity
            {
                Id = Guid.NewGuid(),
                ZoneId = catalogZone.Id,
                Name = versionOwnerName,
                Type = DnsResourceRecordTypeEnum.Txt,
                Class = DnsClassEnum.Internet,
                TimeToLiveSeconds = CatalogRecordTimeToLiveSeconds,
                DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                    typeof(TxtRecordData),
                    new TxtRecordData([CatalogZoneRecordNames.SchemaVersion])
                ),
                ManagedByFeature = RecordManagedByFeatureEnum.Catalog,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
    }

    /// <summary>
    ///     This method builds a member's PTR record.
    /// </summary>
    private static RecordEntity BuildMemberPtrRecord(
        ZoneEntity catalogZone,
        string memberZoneName,
        string uniqueLabel
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            ZoneId = catalogZone.Id,
            Name = CatalogZoneRecordNames.MemberRecordOwnerName(catalogZone.Name, uniqueLabel),
            Type = DnsResourceRecordTypeEnum.Ptr,
            Class = DnsClassEnum.Internet,
            TimeToLiveSeconds = CatalogRecordTimeToLiveSeconds,
            DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                typeof(PtrRecordData),
                new PtrRecordData(DnsName.Parse(memberZoneName))
            ),
            ManagedByFeature = RecordManagedByFeatureEnum.Catalog,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

    /// <summary>
    ///     This method adds any missing <paramref name="peerAddresses" /> to a zone's
    ///     <see cref="ZoneEntity.NotifyAddresses" />. It never removes addresses, since it cannot tell a
    ///     peer's from one an operator added.
    /// </summary>
    /// <returns>Whether the list changed.</returns>
    private static bool MergeNotifyAddresses(ZoneEntity zone, IReadOnlyList<string> peerAddresses)
    {
        if (peerAddresses.Count == 0)
            return false;

        HashSet<string> current = new(zone.NotifyAddresses, StringComparer.OrdinalIgnoreCase);
        bool changed = false;

        foreach (string address in peerAddresses)
            if (current.Add(address))
                changed = true;

        if (changed)
            zone.NotifyAddresses = [.. current];

        return changed;
    }
}
