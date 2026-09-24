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
///     This class is the producer side of RFC 9432 (DNS Catalog Zones): it lets a
///     <see cref="ZoneTypeEnum.Primary" />/<see cref="ZoneTypeEnum.CloudPrimary" /> zone this
///     server originates join or leave a <see cref="ZoneTypeEnum.Catalog" /> zone it also
///     originates, keeping that catalog zone's own well-known records
///     (<see cref="CatalogZoneRecordNames" />) -- and, per the cluster's own "close the loop"
///     design, both zones' own <see cref="ZoneEntity.NotifyAddresses" /> -- in sync with the
///     change. Every actual record write goes through the same <see cref="IZoneUpdateService" />
///     every other content change in this project does, so the resulting catalog zone content
///     transfers to a <see cref="ZoneTypeEnum.SecondaryCatalog" /> peer via the exact same
///     NOTIFY/AXFR/IXFR/TSIG machinery any other secondary zone already uses -- nothing about the
///     catalog zone's own wire transport is new.
///     <para>
///         Deliberately scoped to only the RFC 9432 §5 <c>&lt;unique-N&gt;.zones.$CATZ</c> member
///         PTR entry and the §3 <c>version.$CATZ</c> schema record -- the optional §6 "coo"
///         (change of ownership) and "group"/custom-property records the same spec defines are
///         not produced or consumed here -- this is a deliberate, documented scope cut rather than an oversight.
///     </para>
/// </summary>
/// <param name="zoneRepository">The repository used to look up the catalog and member zones.</param>
/// <param name="recordRepository">The repository used to find a catalog zone's own existing well-known records.</param>
/// <param name="zoneUpdateService">The service every catalog zone record write is actually applied through.</param>
/// <param name="unitOfWork">The unit of work a zone's own metadata (its catalog membership, its NotifyAddresses) is committed through.</param>
/// <param name="peerAddressResolver">The resolver used to derive NotifyAddresses from this node's own known-healthy cluster peers.</param>
public class CatalogZoneMembershipService(
    IZoneRepository zoneRepository,
    IRecordRepository recordRepository,
    IZoneUpdateService zoneUpdateService,
    IUnitOfWork unitOfWork,
    ClusterPeerAddressResolver peerAddressResolver
)
{
    /// <summary>
    ///     This constant contains the time-to-live, in seconds, every catalog record this service
    ///     creates is given. RFC 9432 §3 says a catalog record's TTL "has no meaning ... and
    ///     SHOULD be ignored" by a consumer; <c>0</c> makes that as plain as the spec's own
    ///     examples do, rather than implying a real caching lifetime that does not exist.
    /// </summary>
    private const uint CatalogRecordTimeToLiveSeconds = 0;

    /// <summary>
    ///     This method determines whether a zone's own type is eligible to join a catalog --
    ///     <see cref="ZoneTypeEnum.Primary" /> or <see cref="ZoneTypeEnum.CloudPrimary" />, the
    ///     two types this server is itself the source of truth for. The same
    ///     <c>Primary</c>/<c>CloudPrimary</c>-eligibility shape <c>PtrSyncService</c>'s own
    ///     <c>IsEligibleZoneType</c> and <c>CaaRecordReconciliationService</c>'s own
    ///     <c>IsEligibleZoneType</c> already establish, named identically here on purpose.
    /// </summary>
    public static bool IsEligibleMemberZoneType(ZoneTypeEnum zoneType) =>
        zoneType == ZoneTypeEnum.Primary || zoneType == ZoneTypeEnum.CloudPrimary;

    /// <summary>
    ///     This method lists the member zone names a catalog zone -- either the
    ///     <see cref="ZoneTypeEnum.Catalog" /> original or a <see cref="ZoneTypeEnum.SecondaryCatalog" />
    ///     copy of it -- currently enumerates, parsed live from its own stored PTR records. This
    ///     is what a REST caller (and, in turn, the Angular zone page) uses to actually see a
    ///     catalog's own current membership.
    /// </summary>
    /// <param name="catalogZoneId">The identifier of the catalog (or secondary-catalog) zone to list.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>Every member zone name the catalog currently lists, ordered alphabetically.</returns>
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
    ///     This method adds a zone this server originates as a member of a catalog zone this
    ///     server also originates: writing (or, if this is the catalog's very first member,
    ///     first creating) its own well-known RFC 9432 records, and merging every currently
    ///     healthy cluster peer's own address into both zones' <see cref="ZoneEntity.NotifyAddresses" />
    ///     -- so a future content change to either one reaches every peer with no operator having
    ///     to maintain either list by hand. Idempotent: calling this again for a zone already a
    ///     member of the same catalog is a harmless no-op.
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

        // Every merge below is committed *before* the catalog zone's own content actually
        // changes -- ZoneNotifyEventSubscriber reads the zone fresh, from a detached background
        // task, the moment ApplyChangesAsync publishes DnsZoneChangedEvent below; if the address
        // merge instead happened afterward, that background task could race it and send the very
        // first NOTIFY this membership produces to a stale, pre-merge address list.
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
    ///     This method removes a zone's own membership in whichever catalog zone it currently
    ///     belongs to, deleting its member PTR record from that catalog zone's own content.
    ///     Idempotent: calling this for a zone that is not currently a member of anything is a
    ///     harmless no-op.
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

        // The catalog zone itself may already have been deleted (an operator can delete a
        // Catalog zone without first removing every member from it) -- nothing left to remove a
        // PTR record from in that case; clearing the member zone's own field above is already
        // everything this method owes it.
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
            // The catalog zone has no SOA record to advance -- this should not normally happen
            // for a zone that was able to gain a member in the first place, but if it does,
            // there is nothing further this method can do about it.
        }
    }

    /// <summary>
    ///     This method ensures a catalog zone's own RFC 9432 §3 schema-version TXT record exists,
    ///     adding it to <paramref name="recordsToAdd" /> the first time a member is ever added --
    ///     idempotent, exactly like <see cref="Ptr.PtrSyncService" />'s own reconciliation, so
    ///     calling it again once the record already exists is a harmless no-op.
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
    ///     This method builds a single member's own PTR record.
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
    ///     This method merges every address in <paramref name="peerAddresses" /> into a zone's
    ///     own <see cref="ZoneEntity.NotifyAddresses" />, in place -- additively only, exactly
    ///     mirroring this project's existing "no auto-cleanup when disabled" precedent (CAA/PTR
    ///     auto-management): an address a peer stopped being healthy for, or that an operator
    ///     added by hand for a reason this mechanism knows nothing about, is never removed here.
    /// </summary>
    /// <returns><see langword="true" /> if the zone's own list actually changed.</returns>
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
