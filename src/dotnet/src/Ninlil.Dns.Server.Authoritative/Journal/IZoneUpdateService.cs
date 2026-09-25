using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Journal;

/// <summary>
///     This interface is the path every change to a zone's records should go through. In one
///     save it changes the records, advances the SOA serial and writes a journal entry to
///     <see cref="Data.Abstractions.Repository.IZoneChangeJournalRepository" /> (which IXFR
///     reads), then publishes <see cref="Core.Events.DnsZoneChangedEvent" />. Callers such as the
///     REST API, dynamic update and zone transfer pulls use it rather than writing to
///     <see cref="Data.Abstractions.Repository.IRecordRepository" /> and risking a missed step.
/// </summary>
public interface IZoneUpdateService
{
    /// <summary>
    ///     This method applies record additions and removals as one change: the SOA serial is
    ///     advanced, a journal entry records what changed, and after saving
    ///     <see cref="Core.Events.DnsZoneChangedEvent" /> is published so the caches reload.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone to change.</param>
    /// <param name="recordsToAdd">The records to add. May be empty.</param>
    /// <param name="recordsToRemove">
    ///     The records to remove. They must be the tracked entity instances; removing a detached
    ///     copy would not affect the database.
    /// </param>
    /// <param name="explicitNewSerial">
    ///     The new serial, or <see langword="null" /> to add one to the current serial. A secondary
    ///     applying a transfer passes the primary's serial so the two stay equal.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when <paramref name="zoneId" /> does not name an existing zone, or that zone
    ///     has no SOA record to advance.
    /// </exception>
    Task ApplyChangesAsync(
        Guid zoneId,
        IReadOnlyList<RecordEntity> recordsToAdd,
        IReadOnlyList<RecordEntity> recordsToRemove,
        uint? explicitNewSerial = null,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method stores a zone's first set of records, which <see cref="ApplyChangesAsync" />
    ///     cannot do because it needs an existing SOA to advance. Used, for example, when a
    ///     secondary completes its first pull.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone to provision.</param>
    /// <param name="records">
    ///     Every record the zone starts with, including its SOA record.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when <paramref name="zoneId" /> does not name an existing zone, that zone
    ///     already has records, or <paramref name="records" /> has no SOA record.
    /// </exception>
    Task BootstrapZoneAsync(
        Guid zoneId,
        IReadOnlyList<RecordEntity> records,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method edits one record in place, as the REST record PUT and PATCH endpoints do.
    ///     Unlike a remove-and-add through <see cref="ApplyChangesAsync" />, it keeps the record's
    ///     <see cref="EntityBase.Id" />, which REST clients expect to survive an edit. It still
    ///     journals the before and after versions, which IXFR in
    ///     <see cref="Transfer.ZoneTransferService" /> needs.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone the record belongs to.</param>
    /// <param name="recordId">The identifier of the record to edit.</param>
    /// <param name="applyEdit">
    ///     A callback that changes the tracked record, called after the pre-edit snapshot is taken.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when <paramref name="zoneId" /> does not name an existing zone, that zone has no
    ///     SOA record to advance, or <paramref name="recordId" /> does not name an existing record
    ///     in that zone.
    /// </exception>
    Task ApplyRecordEditAsync(
        Guid zoneId,
        Guid recordId,
        Action<RecordEntity> applyEdit,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method advances a zone's SOA serial without changing any records. The scheduled
    ///     re-sign (<see cref="Dnssec.SignedZoneCache.ResignForScheduledExpiryAsync" />) uses it,
    ///     because new RRSIGs are a change secondaries must notice. Do not use it for record
    ///     changes; <see cref="ApplyChangesAsync" /> and <see cref="ApplyRecordEditAsync" />
    ///     journal a replayable diff.
    ///     <para>
    ///         The journal entry's added and removed lists are empty (<c>"[]"</c>, as for
    ///         <see cref="BootstrapZoneAsync" />): every re-sign recomputes all RRSIGs, so there is
    ///         no useful diff. <see cref="Transfer.ZoneTransferService" /> therefore always answers
    ///         IXFR for a signed zone with a full transfer.
    ///     </para>
    /// </summary>
    /// <param name="zoneId">The identifier of the zone whose serial to advance.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when <paramref name="zoneId" /> does not name an existing zone, or that zone has
    ///     no SOA record to advance.
    /// </exception>
    Task BumpSerialForDnssecResignAsync(Guid zoneId, CancellationToken stoppingToken = default);
}
