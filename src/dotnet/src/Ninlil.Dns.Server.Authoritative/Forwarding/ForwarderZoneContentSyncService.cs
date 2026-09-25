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
///     This class gives a <see cref="ZoneTypeEnum.Forwarder" /> zone records that can be
///     transferred: a synthetic SOA and one TXT record per
///     <see cref="ZoneEntity.ForwarderAddresses" /> entry, at the apex. It reconciles them through
///     <see cref="IZoneUpdateService" /> when <see cref="ForwarderZoneContentSyncEventSubscriber" />
///     reports a zone change. A SecondaryForwarder zone pulls these records by AXFR/IXFR.
///     <para>
///     This is the only code that writes records to a Forwarder zone. Its TXT records carry
///     <see cref="RecordManagedByFeatureEnum.ForwarderAddress" />, and like <c>Ptr.PtrSyncService</c>
///     it only changes records it created, never ones an operator added through
///     <c>RecordsController</c>.
///     </para>
///     <para>
///     It does not run for <see cref="ZoneTypeEnum.SecondaryForwarder" /> zones, whose records come
///     from the transfer pull in <c>Refresh.SecondaryZoneRefreshService</c>; writing them here would
///     fight the pull.
///     </para>
/// </summary>
/// <param name="zoneRepository">The repository used to look up the zone being reconciled.</param>
/// <param name="recordRepository">The repository existing records are read from.</param>
/// <param name="zoneUpdateService">The service every bootstrap, add and remove goes through.</param>
public class ForwarderZoneContentSyncService(
    IZoneRepository zoneRepository,
    IRecordRepository recordRepository,
    IZoneUpdateService zoneUpdateService
)
{
    /// <summary>
    ///     This constant contains the TTL, in seconds, of the synthetic SOA and TXT records.
    /// </summary>
    private const uint RecordTimeToLiveSeconds = 3600;

    private const uint DefaultSoaRefreshSeconds = 3600;
    private const uint DefaultSoaRetrySeconds = 900;
    private const uint DefaultSoaExpireSeconds = 604800;
    private const uint DefaultSoaMinimumTtlSeconds = 3600;

    /// <summary>
    ///     This method makes one zone's apex SOA and TXT records match its
    ///     <see cref="ZoneEntity.ForwarderAddresses" />. It does nothing for zones other than
    ///     <see cref="ZoneTypeEnum.Forwarder" />, or when the records already match, so running it on
    ///     every zone change is harmless.
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
                // Something (a concurrent reconcile, an operator's record write) gave the zone records
                // since the read. That write published DnsZoneChangedEvent, which runs this again against
                // the current state, so ignore the race.
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
    ///     This method builds the synthetic SOA for a new Forwarder zone, like
    ///     <c>Ptr.PtrSyncService.BuildDefaultSoaRecord</c>; the operator can edit it afterwards.
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
    ///     This method builds one TXT record encoding a <see cref="ZoneEntity.ForwarderAddresses" />
    ///     entry with <see cref="ForwarderAddressTxtCodec" />, marked as managed by this feature.
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
