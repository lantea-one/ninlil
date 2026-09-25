using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Journal;

/// <summary>
///     This class implements <see cref="IZoneUpdateService" />. Each operation holds the zone's
///     lock from <see cref="IZoneWriteSerializer" /> while it reads the SOA and writes the
///     change, and ends in <c>FinalizeChangeAsync</c>. Serials advance by one and wrap at
///     2^32 (RFC 1982).
/// </summary>
/// <param name="zoneRepository">The repository used to look up the zone being changed.</param>
/// <param name="recordRepository">The repository the actual record changes are staged through.</param>
/// <param name="journalRepository">The repository the change journal entries are written to.</param>
/// <param name="unitOfWork">The unit of work every staged change is committed through, atomically.</param>
/// <param name="eventPublisher">The publisher used to report the change once it is persisted.</param>
/// <param name="zoneWriteSerializer">
///     The per-zone lock that keeps two changes to the same zone from interleaving (see
///     <see cref="IZoneWriteSerializer" />).
/// </param>
public class ZoneUpdateService(
    IZoneRepository zoneRepository,
    IRecordRepository recordRepository,
    IZoneChangeJournalRepository journalRepository,
    IUnitOfWork unitOfWork,
    IDnsServerEventPublisher eventPublisher,
    IZoneWriteSerializer zoneWriteSerializer
) : IZoneUpdateService
{
    /// <inheritdoc />
    public async Task ApplyChangesAsync(
        Guid zoneId,
        IReadOnlyList<RecordEntity> recordsToAdd,
        IReadOnlyList<RecordEntity> recordsToRemove,
        uint? explicitNewSerial = null,
        CancellationToken stoppingToken = default
    )
    {
        // Hold the zone's lock from reading the SOA to saving, or a concurrent change could
        // read the same serial and both would write the same increment.
        using IDisposable zoneLock = await zoneWriteSerializer.AcquireAsync(zoneId, stoppingToken);

        (ZoneEntity zone, RecordEntity soaRecord, long oldSerial) = await LoadSoaAsync(
            zoneId,
            stoppingToken
        );

        long newSerial = explicitNewSerial ?? (uint)(oldSerial + 1);

        foreach (RecordEntity record in recordsToRemove)
            recordRepository.Remove(record);

        foreach (RecordEntity record in recordsToAdd)
            await recordRepository.AddAsync(record, stoppingToken);

        await FinalizeChangeAsync(
            zone,
            soaRecord,
            oldSerial,
            newSerial,
            recordsToAdd,
            recordsToRemove,
            stoppingToken
        );
    }

    /// <inheritdoc />
    public async Task BootstrapZoneAsync(
        Guid zoneId,
        IReadOnlyList<RecordEntity> records,
        CancellationToken stoppingToken = default
    )
    {
        // Hold the lock across the "no records yet" check and the insert, or two concurrent
        // bootstraps could both see an empty zone.
        using IDisposable zoneLock = await zoneWriteSerializer.AcquireAsync(zoneId, stoppingToken);

        ZoneEntity zone =
            await zoneRepository.GetByIdAsync(zoneId, stoppingToken)
            ?? throw new InvalidOperationException($"No zone with id '{zoneId}' exists.");

        IReadOnlyList<RecordEntity> existingRecords = await recordRepository.ListByZoneIdAsync(
            zoneId,
            stoppingToken
        );

        if (existingRecords.Count > 0)
            throw new InvalidOperationException(
                $"Zone '{zone.Name}' already has records; it cannot be bootstrapped again."
            );

        RecordEntity soaRecord =
            records.FirstOrDefault(record => record.Type == DnsResourceRecordTypeEnum.Soa)
            ?? throw new InvalidOperationException(
                $"Cannot bootstrap zone '{zone.Name}' without an SOA record."
            );

        if (RecordEntityCodec.DecodeTypedData(soaRecord) is not SoaRecordData soaData)
            throw new InvalidOperationException(
                $"Zone '{zone.Name}''s SOA record could not be decoded."
            );

        foreach (RecordEntity record in records)
            await recordRepository.AddAsync(record, stoppingToken);

        await journalRepository.AddAsync(
            new ZoneChangeJournalEntryEntity
            {
                Id = Guid.NewGuid(),
                ZoneId = zoneId,
                // 0 stands for "no previous serial" in the journal.
                OldSerial = 0,
                NewSerial = soaData.Serial,
                RemovedRecordsJson = "[]",
                AddedRecordsJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(records),
                CreatedAtUtc = DateTimeOffset.UtcNow,
            },
            stoppingToken
        );

        await unitOfWork.SaveChangesAsync(stoppingToken);

        eventPublisher.Publish(new DnsZoneChangedEvent(DateTimeOffset.UtcNow, zoneId, zone.Name));
    }

    /// <inheritdoc />
    public async Task ApplyRecordEditAsync(
        Guid zoneId,
        Guid recordId,
        Action<RecordEntity> applyEdit,
        CancellationToken stoppingToken = default
    )
    {
        // Hold the zone's lock for the same reason as ApplyChangesAsync.
        using IDisposable zoneLock = await zoneWriteSerializer.AcquireAsync(zoneId, stoppingToken);

        (ZoneEntity zone, RecordEntity soaRecord, long oldSerial) = await LoadSoaAsync(
            zoneId,
            stoppingToken
        );

        RecordEntity record =
            await recordRepository.GetByIdAsync(recordId, stoppingToken)
            ?? throw new InvalidOperationException(
                $"No record with id '{recordId}' exists in zone '{zone.Name}'."
            );

        // Editing the SOA itself (done through the ordinary record PUT/PATCH) means `record`
        // and `soaRecord` are the same row. On MongoDB, which has no identity map, they are two
        // separate copies. FinalizeChangeAsync writes the SOA from the object it is given, so
        // pass `record`, which carries the edit; `soaRecord` would drop it.
        bool editIsToTheSoaRecordItself = record.Id == soaRecord.Id;
        RecordEntity soaRecordToFinalize = editIsToTheSoaRecordItself ? record : soaRecord;

        // Snapshot the record before applyEdit changes it, so the journal (and IXFR) has the
        // old version.
        RecordEntity beforeSnapshot = new()
        {
            Id = record.Id,
            ZoneId = record.ZoneId,
            Name = record.Name,
            Type = record.Type,
            Class = record.Class,
            TimeToLiveSeconds = record.TimeToLiveSeconds,
            DataJson = record.DataJson,
            IsDisabled = record.IsDisabled,
            Comments = record.Comments,
            ExcludeFromSync = record.ExcludeFromSync,
            CreatedAtUtc = record.CreatedAtUtc,
            UpdatedAtUtc = record.UpdatedAtUtc,
        };

        applyEdit(record);
        record.UpdatedAtUtc = DateTimeOffset.UtcNow;
        recordRepository.Update(record);

        long newSerial = (uint)(oldSerial + 1);

        // An SOA edit is not journaled as added/removed content: IXFR
        // (ZoneTransferService.BuildHistoricalSoaWireRecord) builds the SOA bookends from
        // OldSerial/NewSerial, and SOA records in the diff body would add two more. IXFR
        // therefore assumes other SOA fields never change.
        IReadOnlyList<RecordEntity> added = editIsToTheSoaRecordItself ? [] : [record];
        IReadOnlyList<RecordEntity> removed = editIsToTheSoaRecordItself ? [] : [beforeSnapshot];

        await FinalizeChangeAsync(
            zone,
            soaRecordToFinalize,
            oldSerial,
            newSerial,
            added,
            removed,
            stoppingToken
        );
    }

    /// <inheritdoc />
    public async Task BumpSerialForDnssecResignAsync(
        Guid zoneId,
        CancellationToken stoppingToken = default
    )
    {
        // Hold the zone's lock for the same reason as ApplyChangesAsync.
        using IDisposable zoneLock = await zoneWriteSerializer.AcquireAsync(zoneId, stoppingToken);

        (ZoneEntity zone, RecordEntity soaRecord, long oldSerial) = await LoadSoaAsync(
            zoneId,
            stoppingToken
        );

        long newSerial = (uint)(oldSerial + 1);

        // No records change, so journal empty lists.
        await FinalizeChangeAsync(zone, soaRecord, oldSerial, newSerial, [], [], stoppingToken);
    }

    /// <summary>
    ///     This method loads a zone and its SOA record and decodes the current serial.
    /// </summary>
    private async Task<(ZoneEntity Zone, RecordEntity SoaRecord, long OldSerial)> LoadSoaAsync(
        Guid zoneId,
        CancellationToken stoppingToken
    )
    {
        ZoneEntity zone =
            await zoneRepository.GetByIdAsync(zoneId, stoppingToken)
            ?? throw new InvalidOperationException($"No zone with id '{zoneId}' exists.");

        IReadOnlyList<RecordEntity> soaRecords = await recordRepository.FindByNameAsync(
            zoneId,
            zone.Name,
            DnsResourceRecordTypeEnum.Soa,
            stoppingToken
        );

        RecordEntity soaRecord =
            soaRecords.FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"Zone '{zone.Name}' has no SOA record to increment."
            );

        if (RecordEntityCodec.DecodeTypedData(soaRecord) is not SoaRecordData soaData)
            throw new InvalidOperationException(
                $"Zone '{zone.Name}' has an SOA record that could not be decoded."
            );

        return (zone, soaRecord, soaData.Serial);
    }

    /// <summary>
    ///     This method finishes a change: it writes the new serial into the SOA, adds the journal
    ///     entry, saves everything in one <see cref="IUnitOfWork.SaveChangesAsync" /> call and
    ///     then publishes <see cref="DnsZoneChangedEvent" />.
    /// </summary>
    private async Task FinalizeChangeAsync(
        ZoneEntity zone,
        RecordEntity soaRecord,
        long oldSerial,
        long newSerial,
        IReadOnlyList<RecordEntity> added,
        IReadOnlyList<RecordEntity> removed,
        CancellationToken stoppingToken
    )
    {
        if (RecordEntityCodec.DecodeTypedData(soaRecord) is not SoaRecordData soaData)
            throw new InvalidOperationException(
                $"Zone '{zone.Name}' has an SOA record that could not be decoded."
            );

        soaRecord.DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
            soaData with
            {
                Serial = (uint)newSerial,
            }
        );
        soaRecord.UpdatedAtUtc = DateTimeOffset.UtcNow;
        recordRepository.Update(soaRecord);

        await journalRepository.AddAsync(
            new ZoneChangeJournalEntryEntity
            {
                Id = Guid.NewGuid(),
                ZoneId = zone.Id,
                OldSerial = oldSerial,
                NewSerial = newSerial,
                RemovedRecordsJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(removed),
                AddedRecordsJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(added),
                CreatedAtUtc = DateTimeOffset.UtcNow,
            },
            stoppingToken
        );

        await unitOfWork.SaveChangesAsync(stoppingToken);

        eventPublisher.Publish(new DnsZoneChangedEvent(DateTimeOffset.UtcNow, zone.Id, zone.Name));
    }
}
