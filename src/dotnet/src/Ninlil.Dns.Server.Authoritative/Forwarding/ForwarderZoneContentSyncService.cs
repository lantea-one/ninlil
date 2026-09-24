using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.Journal;
using Ninlil.Dns.Server.Core.Forwarding;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Forwarding;

/// <summary>
///     This class gives a <see cref="ZoneTypeEnum.Forwarder" /> zone real, versioned,
///     AXFR/IXFR-transferable wire content for the first time: a synthetic SOA record, and one
///     <see cref="DnsResourceRecordTypeEnum.Txt" /> record per configured
///     <see cref="ZoneEntity.ForwarderAddresses" /> entry, both at the zone's own apex --
///     reconciled through <see cref="IZoneUpdateService" /> (the one path this project's own zone
///     content ever changes through) whenever <see cref="Core.Events.DnsZoneChangedEvent" />
///     reports the zone's own metadata changed, via <see cref="ForwarderZoneContentSyncEventSubscriber" />.
///     <para>
///         This is deliberately the *only* place a <see cref="ZoneTypeEnum.Forwarder" /> zone's
///         own <see cref="RecordEntity" /> rows are written at all -- mirroring
///         <c>Ptr.PtrSyncService</c>'s own "reconcile only records this feature itself created"
///         discipline, every managed TXT record here carries
///         <see cref="RecordManagedByFeatureEnum.ForwarderAddress" /> so a future reconciliation
///         pass never mistakes (or touches) a record an operator might have added to this zone by
///         hand through the ordinary <c>RecordsController</c>.
///     </para>
///     <para>
///         Never runs for a <see cref="ZoneTypeEnum.SecondaryForwarder" /> zone -- that zone
///         type's own apex content arrives entirely through
///         <c>Refresh.SecondaryZoneRefreshService</c>'s own AXFR/IXFR pull from its configured
///         <see cref="ZoneTypeEnum.Forwarder" /> primary, exactly like an ordinary
///         <see cref="ZoneTypeEnum.Secondary" /> zone's own record content; this service would
///         otherwise fight that pull for ownership of the same records.
///     </para>
/// </summary>
/// <param name="zoneRepository">The repository used to look up the zone being reconciled.</param>
/// <param name="recordRepository">This service's own existing-record lookups run against this repository.</param>
/// <param name="zoneUpdateService">The service every bootstrap/add/remove is actually applied through.</param>
public class ForwarderZoneContentSyncService(
    IZoneRepository zoneRepository,
    IRecordRepository recordRepository,
    IZoneUpdateService zoneUpdateService
)
{
    /// <summary>
    ///     This constant contains the time-to-live, in seconds, this service's own synthetic SOA
    ///     and TXT records are given -- matching <c>Ptr.PtrSyncService</c>'s own default TTL for
    ///     an infrequently-changing, feature-managed record.
    /// </summary>
    private const uint RecordTimeToLiveSeconds = 3600;

    private const uint DefaultSoaRefreshSeconds = 3600;
    private const uint DefaultSoaRetrySeconds = 900;
    private const uint DefaultSoaExpireSeconds = 604800;
    private const uint DefaultSoaMinimumTtlSeconds = 3600;

    /// <summary>
    ///     This method reconciles a single zone's own apex SOA/TXT content against its current
    ///     <see cref="ZoneEntity.ForwarderAddresses" /> -- the entry point
    ///     <see cref="ForwarderZoneContentSyncEventSubscriber" /> calls in response to
    ///     <see cref="Core.Events.DnsZoneChangedEvent" />. A no-op for any zone this feature does
    ///     not own the content of (anything other than <see cref="ZoneTypeEnum.Forwarder" />), and
    ///     a no-op once the zone's own stored TXT records already match its configured addresses
    ///     -- so this can safely run again on every unrelated zone-metadata change too, the same
    ///     "cheap, idempotent no-op once already in sync" property every other
    ///     <see cref="Core.Events.DnsZoneChangedEvent" /> subscriber in this project relies on.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone to reconcile.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public async Task ReconcileZoneAsync(Guid zoneId, CancellationToken stoppingToken = default)
    {
        ZoneEntity zone = await zoneRepository.GetByIdAsync(zoneId, stoppingToken);

        if (zone is null || zone.Type != ZoneTypeEnum.Forwarder)
            return;

        IReadOnlyList<RecordEntity> existingRecords = await recordRepository.ListByZoneIdAsync(
            zoneId,
            stoppingToken
        );

        List<(int Index, string Address)> desired =
        [
            .. zone.ForwarderAddresses.Select((address, index) => (index, address)),
        ];

        bool hasSoa = existingRecords.Any(record => record.Type == DnsResourceRecordTypeEnum.Soa);

        if (!hasSoa)
        {
            List<RecordEntity> initialRecords =
            [
                BuildSoaRecord(zone),
                .. desired.Select(entry => BuildTxtRecord(zone, entry.Index, entry.Address)),
            ];

            try
            {
                await zoneUpdateService.BootstrapZoneAsync(zone.Id, initialRecords, stoppingToken);
            }
            catch (InvalidOperationException)
            {
                // Raced with something else that gave this zone records between the read above
                // and this write (most plausibly a concurrent reconcile of the same zone, or an
                // operator writing a record through RecordsController in between) -- the next
                // DnsZoneChangedEvent this zone's own write just published retries reconciliation
                // against whatever now actually exists, rather than this call failing loudly for
                // what is, in practice, a harmless timing race.
            }

            return;
        }

        List<RecordEntity> existingManaged =
        [
            .. existingRecords.Where(record =>
                record.Type == DnsResourceRecordTypeEnum.Txt
                && record.Name == zone.Name
                && record.ManagedByFeature == RecordManagedByFeatureEnum.ForwarderAddress
            ),
        ];

        List<(RecordEntity Entity, int Index, string Address)> existingDecoded =
        [
            .. existingManaged
                .Select(record => (Entity: record, Data: RecordEntityCodec.DecodeTypedData(record)))
                .Where(pair => pair.Data is TxtRecordData)
                .Select(pair =>
                    (
                        pair.Entity,
                        Decoded: ForwarderAddressTxtCodec.TryDecode(
                            (TxtRecordData)pair.Data,
                            out int index,
                            out string address
                        ),
                        Index: index,
                        Address: address
                    )
                )
                .Where(triple => triple.Decoded)
                .Select(triple => (triple.Entity, triple.Index, triple.Address)),
        ];

        HashSet<(int Index, string Address)> desiredKeys = [.. desired];
        HashSet<(int Index, string Address)> existingKeys =
        [
            .. existingDecoded.Select(entry => (entry.Index, entry.Address)),
        ];

        List<RecordEntity> toRemove =
        [
            .. existingDecoded
                .Where(entry => !desiredKeys.Contains((entry.Index, entry.Address)))
                .Select(entry => entry.Entity),
        ];
        List<RecordEntity> toAdd =
        [
            .. desired
                .Where(entry => !existingKeys.Contains(entry))
                .Select(entry => BuildTxtRecord(zone, entry.Index, entry.Address)),
        ];

        if (toAdd.Count == 0 && toRemove.Count == 0)
            return;

        await zoneUpdateService.ApplyChangesAsync(zone.Id, toAdd, toRemove, null, stoppingToken);
    }

    /// <summary>
    ///     This method builds a fresh synthetic SOA record for a brand-new Forwarder zone's own
    ///     apex, the same "auto-populated, then operator-editable exactly like any other record"
    ///     shape <c>Ptr.PtrSyncService.BuildDefaultSoaRecord</c> already establishes for an
    ///     auto-created reverse zone.
    /// </summary>
    private static RecordEntity BuildSoaRecord(ZoneEntity zone) =>
        new()
        {
            Id = Guid.NewGuid(),
            ZoneId = zone.Id,
            Name = zone.Name,
            Type = DnsResourceRecordTypeEnum.Soa,
            Class = DnsClassEnum.Internet,
            TimeToLiveSeconds = RecordTimeToLiveSeconds,
            DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                typeof(SoaRecordData),
                new SoaRecordData(
                    DnsName.Parse(zone.Name),
                    DnsName.Parse($"hostmaster.{zone.Name}"),
                    1,
                    DefaultSoaRefreshSeconds,
                    DefaultSoaRetrySeconds,
                    DefaultSoaExpireSeconds,
                    DefaultSoaMinimumTtlSeconds
                )
            ),
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

    /// <summary>
    ///     This method builds a single desired TXT record, encoding one
    ///     <see cref="ZoneEntity.ForwarderAddresses" /> entry via
    ///     <see cref="ForwarderAddressTxtCodec" /> and marking it with this feature's own
    ///     provenance marker.
    /// </summary>
    private static RecordEntity BuildTxtRecord(ZoneEntity zone, int index, string address) =>
        new()
        {
            Id = Guid.NewGuid(),
            ZoneId = zone.Id,
            Name = zone.Name,
            Type = DnsResourceRecordTypeEnum.Txt,
            Class = DnsClassEnum.Internet,
            TimeToLiveSeconds = RecordTimeToLiveSeconds,
            DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                typeof(TxtRecordData),
                ForwarderAddressTxtCodec.Encode(index, address)
            ),
            ManagedByFeature = RecordManagedByFeatureEnum.ForwarderAddress,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
}
