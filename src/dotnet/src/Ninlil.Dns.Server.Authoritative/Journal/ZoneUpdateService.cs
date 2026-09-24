using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Journal;

/// <summary>
///     This class implements <see cref="IZoneUpdateService" />.
/// </summary>
/// <param name="zoneRepository">The repository used to look up the zone being changed.</param>
/// <param name="recordRepository">The repository the actual record changes are staged through.</param>
/// <param name="journalRepository">The repository this service's own history entry is written to.</param>
/// <param name="unitOfWork">The unit of work every staged change is committed through, atomically.</param>
/// <param name="eventPublisher">The publisher used to report the change once it is persisted.</param>
/// <param name="zoneWriteSerializer">
///     The serializer used to keep two concurrent changes to the same zone from interleaving
///     their own read of that zone's current state with each other's write of a new one -- see
///     its own remarks for the exact failure shapes this prevents.
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
        // Hold this zone's own lock across the entire read-current-SOA-then-write-new-SOA
        // sequence below -- without it, a concurrent call changing the same zone (another
        // ApplyChangesAsync, an ApplyRecordEditAsync, or a BootstrapZoneAsync racing a
        // just-created zone's very first record) could read the same "current" serial this call
        // just read, before either has committed its own increment, and the two writes would
        // silently clobber one another down to a single increment for two real content changes.
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
        // Hold this zone's own lock across the "does this zone already have records" check and
        // the records this call adds in response to it -- without it, two concurrent callers
        // bootstrapping (or one bootstrapping while another applies an ordinary change to) the
        // same brand-new zone could both observe zero existing records before either commits,
        // and both proceed, one succeeding and the other left in an inconsistent, hard-to-explain
        // state depending on exactly how the two interleaved.
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
                // There is no meaningful "before" serial for a zone that had no data at all; 0
                // is a sentinel meaning exactly that, matching how a from-scratch SOA serial
                // would never legitimately be 0 either.
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
        // Hold this zone's own lock for the same reason ApplyChangesAsync does -- an edit reads
        // and then advances the zone's current serial exactly the same way a bulk add/remove
        // does, so it is just as vulnerable to a concurrent change to the same zone reading and
        // clobbering the same "current" serial in between.
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

        // A caller editing the zone's own SOA record directly (RecordsController's PUT/PATCH
        // endpoints explicitly document this as the supported path -- there is no separate
        // "edit the SOA" action) names the very same row LoadSoaAsync already loaded above as
        // `soaRecord`. On a storage engine with no EF-Core-style identity map (MongoDB), `record`
        // and `soaRecord` are then two independently loaded, independently mutable copies of one
        // row, not the same tracked instance -- the same stale-copy hazard as EF Core's identity map
        // across scopes, here between two objects loaded once each in the same call.
        // FinalizeChangeAsync below always recomputes the persisted SOA content from whichever
        // object it is handed, so that object must be `record` (carrying this call's own edit,
        // applied a few lines down) whenever the two alias the same row -- handing it the
        // edit-blind `soaRecord` instead would silently discard this call's own edit, surviving
        // only as far as the serial bump.
        bool editIsToTheSoaRecordItself = record.Id == soaRecord.Id;
        RecordEntity soaRecordToFinalize = editIsToTheSoaRecordItself ? record : soaRecord;

        // A detached, pre-edit snapshot -- not the tracked instance itself, which applyEdit is
        // about to mutate -- is what the journal needs to record an accurate "before" value.
        // Without this, the journal would show an empty removed/added diff for a real change,
        // leaving ZoneTransferService's own IXFR responder with a bumped serial but no
        // corresponding delta to serve for it.
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

        // The SOA's own identity crosses to a secondary purely through OldSerial/NewSerial --
        // ZoneTransferService's own incremental-diff replay (BuildHistoricalSoaWireRecord)
        // already assumes every other SOA field stays constant across journal history, so the
        // SOA itself must never also appear as ordinary added/removed diff content within a
        // difference sequence's own body (which would otherwise mean four SOA records -- two
        // real bookends plus these two -- where a validator expects exactly two). An edit to any
        // other record is journaled exactly as before.
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
        // Hold this zone's own lock for the same reason every other content change does -- a
        // proactive re-sign reads and then advances the zone's current serial exactly the same
        // way an ordinary edit does, so it is just as vulnerable to racing a concurrent change to
        // the same zone.
        using IDisposable zoneLock = await zoneWriteSerializer.AcquireAsync(zoneId, stoppingToken);

        (ZoneEntity zone, RecordEntity soaRecord, long oldSerial) = await LoadSoaAsync(
            zoneId,
            stoppingToken
        );

        long newSerial = (uint)(oldSerial + 1);

        // Unlike ApplyChangesAsync/ApplyRecordEditAsync, there is no added/removed RecordEntity
        // content to journal here at all -- a proactive re-sign never touches this zone's own
        // stored records, only the in-memory RRSIGs SignedZoneCache computes for it -- so this
        // reuses FinalizeChangeAsync directly with empty lists, never loading a second object for
        // the SOA row the way ApplyRecordEditAsync must, and so never at risk of that method's own
        // aliasing race in the first place.
        await FinalizeChangeAsync(zone, soaRecord, oldSerial, newSerial, [], [], stoppingToken);
    }

    /// <summary>
    ///     This method loads a zone and its current SOA record, decoding the SOA's own serial --
    ///     the read-before-mutate step every content change (a bulk add/remove, a bootstrap, or a
    ///     single record edit) needs before it can compute a new serial.
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
    ///     This method finishes any content change: bumping the SOA's own stored serial, writing
    ///     the journal entry that records exactly what changed, committing every staged change in
    ///     one atomic <see cref="IUnitOfWork.SaveChangesAsync" /> call, and publishing
    ///     <see cref="DnsZoneChangedEvent" /> once that commit succeeds.
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
