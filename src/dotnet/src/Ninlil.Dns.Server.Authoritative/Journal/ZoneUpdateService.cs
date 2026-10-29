using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
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
        CancellationToken stoppingToken = default,
        DnsResourceRecord replacementSoa = null
    )
    {
        // Hold the zone's lock from reading the SOA to saving, or a concurrent change could
        // read the same serial and both would write the same increment.
        using IDisposable zoneLock = await zoneWriteSerializer.AcquireAsync(zoneId, stoppingToken);

        (ZoneEntity zone, RecordEntity soaRecord, long oldSerial, RecordEntity oldSoaSnapshot) =
            await LoadSoaAsync(zoneId, stoppingToken);

        long newSerial = explicitNewSerial ?? (uint)(oldSerial + 1);

        foreach (RecordEntity record in recordsToRemove)
            recordRepository.Remove(record);

        foreach (RecordEntity record in recordsToAdd)
            await recordRepository.AddAsync(record, stoppingToken);

        await FinalizeChangeAsync(
            zone,
            soaRecord,
            oldSerial,
            oldSoaSnapshot,
            newSerial,
            recordsToAdd,
            recordsToRemove,
            stoppingToken,
            replacementSoa
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
                // A new zone has no older SOA, so only the new side is recorded.
                OldSoaRecordJson = null,
                NewSoaRecordJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                    SnapshotSoa(soaRecord)
                ),
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

        (ZoneEntity zone, RecordEntity soaRecord, long oldSerial, RecordEntity oldSoaSnapshot) =
            await LoadSoaAsync(zoneId, stoppingToken);

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

        // RFC 1034 §4.2.1: a zone has exactly one SOA, at its apex. An edit that adds, removes or
        // moves it would leave the zone without a findable SOA. Nothing has been saved yet, so
        // throwing here discards the edit.
        bool typeChanged = beforeSnapshot.Type != record.Type;
        bool soaInvolved =
            beforeSnapshot.Type == DnsResourceRecordTypeEnum.Soa
            || record.Type == DnsResourceRecordTypeEnum.Soa;
        bool soaRenamed =
            beforeSnapshot.Type == DnsResourceRecordTypeEnum.Soa
            && !string.Equals(
                NormalizeOwnerName(beforeSnapshot.Name),
                NormalizeOwnerName(record.Name),
                StringComparison.OrdinalIgnoreCase
            );
        if ((typeChanged && soaInvolved) || soaRenamed)
            throw new InvalidOperationException(
                "An edit cannot change a record's type to or from SOA, or rename the SOA record: a zone has exactly one SOA, at its apex. Edit the SOA's fields instead."
            );

        record.UpdatedAtUtc = DateTimeOffset.UtcNow;
        recordRepository.Update(record);

        long newSerial = (uint)(oldSerial + 1);

        // An SOA edit is a step whose change is carried by its two SOA bookends (the old and
        // new SOA snapshots of the journal entry), so its added and removed lists stay empty:
        // SOA records in the diff body would add two more SOAs to the difference sequence.
        IReadOnlyList<RecordEntity> added = editIsToTheSoaRecordItself ? [] : [record];
        IReadOnlyList<RecordEntity> removed = editIsToTheSoaRecordItself ? [] : [beforeSnapshot];

        await FinalizeChangeAsync(
            zone,
            soaRecordToFinalize,
            oldSerial,
            oldSoaSnapshot,
            newSerial,
            added,
            removed,
            stoppingToken
        );
    }

    /// <summary>
    ///     This method drops one trailing dot so that <c>example.com.</c> and <c>example.com</c>
    ///     name the same owner.
    /// </summary>
    private static string NormalizeOwnerName(string name) => (name ?? string.Empty).TrimEnd('.');

    /// <inheritdoc />
    public async Task BumpSerialForDnssecResignAsync(
        Guid zoneId,
        CancellationToken stoppingToken = default
    )
    {
        // Hold the zone's lock for the same reason as ApplyChangesAsync.
        using IDisposable zoneLock = await zoneWriteSerializer.AcquireAsync(zoneId, stoppingToken);

        (ZoneEntity zone, RecordEntity soaRecord, long oldSerial, RecordEntity oldSoaSnapshot) =
            await LoadSoaAsync(zoneId, stoppingToken);

        long newSerial = (uint)(oldSerial + 1);

        // No records change, so journal empty lists.
        await FinalizeChangeAsync(
            zone,
            soaRecord,
            oldSerial,
            oldSoaSnapshot,
            newSerial,
            [],
            [],
            stoppingToken
        );
    }

    /// <summary>
    ///     This method copies the parts of an SOA record that make up the SOA RR a journal entry
    ///     records: owner name, type, class, TTL and data. The copy is detached, so a later change
    ///     to <paramref name="soaRecord" /> (EF tracks and mutates the loaded object) does not
    ///     reach it.
    /// </summary>
    private static RecordEntity SnapshotSoa(RecordEntity soaRecord) =>
        new()
        {
            Name = soaRecord.Name,
            Type = soaRecord.Type,
            Class = soaRecord.Class,
            TimeToLiveSeconds = soaRecord.TimeToLiveSeconds,
            DataJson = soaRecord.DataJson,
        };

    /// <summary>
    ///     This method loads a zone and its SOA record and decodes the current serial. It also
    ///     returns a snapshot of the SOA as loaded, taken before any caller mutates the record, for
    ///     the journal entry's old SOA.
    /// </summary>
    private async Task<(
        ZoneEntity Zone,
        RecordEntity SoaRecord,
        long OldSerial,
        RecordEntity OldSoaSnapshot
    )> LoadSoaAsync(Guid zoneId, CancellationToken stoppingToken)
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

        return (zone, soaRecord, soaData.Serial, SnapshotSoa(soaRecord));
    }

    /// <summary>
    ///     This method finishes a change: it writes the new serial into the SOA, adds the journal
    ///     entry, saves everything in one <see cref="IUnitOfWork.SaveChangesAsync" /> call and
    ///     then publishes <see cref="DnsZoneChangedEvent" />. The entry records
    ///     <paramref name="oldSoaSnapshot" /> and a snapshot of the SOA as written here. When <paramref name="replacementSoa" />
    ///     is set, the SOA's other fields and its TTL are replaced by that record's too.
    /// </summary>
    private async Task FinalizeChangeAsync(
        ZoneEntity zone,
        RecordEntity soaRecord,
        long oldSerial,
        RecordEntity oldSoaSnapshot,
        long newSerial,
        IReadOnlyList<RecordEntity> added,
        IReadOnlyList<RecordEntity> removed,
        CancellationToken stoppingToken,
        DnsResourceRecord replacementSoa = null
    )
    {
        if (RecordEntityCodec.DecodeTypedData(soaRecord) is not SoaRecordData soaData)
            throw new InvalidOperationException(
                $"Zone '{zone.Name}' has an SOA record that could not be decoded."
            );

        if (replacementSoa is not null)
        {
            // The stored SOA must equal the primary's (RFC 1034 §4.3.5), so its data and TTL come
            // from the transferred record. The serial below stays the caller's explicit one.
            RecordEntity replacement = RecordEntityCodec.FromWireRecord(replacementSoa, zone.Id);
            if (RecordEntityCodec.DecodeTypedData(replacement) is not SoaRecordData replacementData)
                throw new InvalidOperationException(
                    $"The replacement SOA for zone '{zone.Name}' could not be decoded."
                );

            soaData = replacementData;
            soaRecord.TimeToLiveSeconds = replacement.TimeToLiveSeconds;
        }

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
                OldSoaRecordJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                    oldSoaSnapshot
                ),
                NewSoaRecordJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                    SnapshotSoa(soaRecord)
                ),
                CreatedAtUtc = DateTimeOffset.UtcNow,
            },
            stoppingToken
        );

        await unitOfWork.SaveChangesAsync(stoppingToken);

        eventPublisher.Publish(new DnsZoneChangedEvent(DateTimeOffset.UtcNow, zone.Id, zone.Name));
    }
}
