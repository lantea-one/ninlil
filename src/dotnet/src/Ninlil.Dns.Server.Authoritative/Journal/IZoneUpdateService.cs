using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Journal;

/// <summary>
///     This interface is the one path this project expects a zone's record content to ever
///     change through -- bumping the zone's own SOA serial and recording the change in
///     <see cref="Data.Abstractions.Repository.IZoneChangeJournalRepository" /> atomically,
///     alongside the actual record changes, rather than something a caller (the dynamic update
///     handler, or the REST API) does by hand against
///     <see cref="Data.Abstractions.Repository.IRecordRepository" /> directly and might forget a
///     step of.
/// </summary>
public interface IZoneUpdateService
{
    /// <summary>
    ///     This method applies a set of record additions and removals to a zone as a single,
    ///     atomic change: the zone's SOA serial is advanced, a journal entry recording exactly
    ///     what changed is written, and, once persisted, <see cref="Core.Events.DnsZoneChangedEvent" />
    ///     is published so <see cref="Cache.AuthoritativeZoneCache" /> picks the change up.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone to change.</param>
    /// <param name="recordsToAdd">The records to add. May be empty.</param>
    /// <param name="recordsToRemove">
    ///     The records to remove -- the actual tracked entity instances, not copies, since
    ///     removing a detached copy would not affect the database.
    /// </param>
    /// <param name="explicitNewSerial">
    ///     The serial to advance to, or <see langword="null" /> to simply increment the current
    ///     one by one -- the right default when this server itself is the source of truth for
    ///     the change. A secondary zone applying a transfer pulled from its primary instead
    ///     supplies the primary's own new serial here, adopting it exactly rather than computing
    ///     one independently, since the two must always agree.
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
    ///     This method provisions a zone's very first set of records -- the one case
    ///     <see cref="ApplyChangesAsync" /> cannot handle, since it requires an existing SOA
    ///     record to advance and a brand-new zone has none yet. Used when a secondary zone
    ///     completes its first-ever pull from its primary.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone to provision.</param>
    /// <param name="records">
    ///     Every record the zone should start with, including its own SOA record.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when <paramref name="zoneId" /> does not name an existing zone, that zone
    ///     already has records of its own, or <paramref name="records" /> carries no SOA record.
    /// </exception>
    Task BootstrapZoneAsync(
        Guid zoneId,
        IReadOnlyList<RecordEntity> records,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method edits a single existing record in place -- the one path a caller (the REST
    ///     API's own record PUT/PATCH endpoints, most immediately) uses to change a record's
    ///     fields without changing its own identity. Unlike expressing an edit as a
    ///     <see cref="ApplyChangesAsync" /> remove-then-add pair with a fresh
    ///     <see cref="EntityBase.Id" />, this keeps the record's own <c>Id</c> stable (an
    ///     ordinary REST client expects a resource's identity to survive its own PUT/PATCH), while
    ///     still journaling an accurate before/after snapshot of exactly what changed -- the same
    ///     snapshot <see cref="Data.Abstractions.Repository.IZoneChangeJournalRepository" />'s own
    ///     IXFR consumer (<see cref="Transfer.ZoneTransferService" />) needs, which an in-place
    ///     mutation with no snapshot at all would otherwise leave inaccurate (a bumped serial with
    ///     no corresponding recorded delta).
    /// </summary>
    /// <param name="zoneId">The identifier of the zone the record belongs to.</param>
    /// <param name="recordId">The identifier of the record to edit.</param>
    /// <param name="applyEdit">
    ///     A callback that mutates the record's own tracked instance directly -- called after
    ///     this method has already captured a pre-edit snapshot for the journal.
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
    ///     This method advances a zone's own SOA serial with no accompanying record content
    ///     change at all -- the one path <see cref="Dnssec.SignedZoneCache" />'s own
    ///     schedule-driven proactive re-sign (see <see cref="Dnssec.DnssecResigningBackgroundService" />)
    ///     uses to tell a secondary a genuine content change happened, even though nothing about
    ///     this zone's own stored <see cref="RecordEntity" /> rows changed at all: real-world DNS
    ///     convention treats a re-sign as a real change (fresh RRSIGs a secondary must notice),
    ///     matching this project's own confirmed decision. Never call this for an ordinary content
    ///     change -- use <see cref="ApplyChangesAsync" /> or <see cref="ApplyRecordEditAsync" />
    ///     instead, both of which journal a real, replayable before/after diff.
    ///     <para>
    ///         The resulting journal entry's own Added/Removed content is deliberately left
    ///         empty, the same <c>"[]"</c> sentinel <see cref="BootstrapZoneAsync" /> already uses
    ///         for "no meaningful content list" -- this project's DNSSEC signing design
    ///         recomputes every RRSIG from scratch on every pass (a fresh inception/expiration
    ///         window even for an RRset whose own data never changed), so there is no meaningful
    ///         byte-level "old vs new" signed content to journal for a true incremental replay.
    ///         <see cref="Transfer.ZoneTransferService" /> recognizes a signed zone for exactly
    ///         this reason and always serves its own IXFR as a full transfer instead, carrying
    ///         this zone's complete, current signed content fresh from
    ///         <see cref="Dnssec.SignedZoneCache" /> every time.
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
