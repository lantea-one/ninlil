using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.Journal;
using Ninlil.Dns.Server.Authoritative.Options;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Ptr;

/// <summary>
///     This class is the actual PTR/reverse-zone sync engine: it computes the desired PTR
///     record set implied by a <see cref="ZoneTypeEnum.Primary" />/
///     <see cref="ZoneTypeEnum.CloudPrimary" /> forward zone's own current, enabled A/AAAA
///     records, auto-creates the enclosing reverse zone the first time it is needed (never a
///     second, conflicting copy of one that already exists), and reconciles that reverse zone's
///     actual stored PTR records to match -- touching only records this feature created itself
///     (<see cref="RecordEntity.ManagedByFeature" /> == <see cref="RecordManagedByFeatureEnum.Ptr" />),
///     never an operator-added PTR record.
///     <para>
///         When a forward zone's own IPv4 grouping granularity (<see cref="ZoneEntity.PtrIpv4PrefixLength" />,
///         falling back to <see cref="PtrSyncOptions.DefaultIpv4ReverseZonePrefixBits" />) is set
///         finer than the conventional <c>/24</c> octet boundary, this class also maintains RFC
///         2317 §4's own classless-delegation glue: the octet-aligned <c>/24</c> parent zone gains
///         a CNAME record, at the same owner name an ordinary (non-classless) PTR record would
///         have used, pointing at the classless child zone's own owner name for that address --
///         reusing <see cref="RecordManagedByFeatureEnum.Ptr" /> as this glue record's own
///         provenance marker too (a CNAME record with that marker is unambiguous -- an ordinary
///         PTR-sync PTR record is never itself a CNAME), rather than adding a second enum value
///         for what is still, functionally, this same feature's own output.
///     </para>
///     <para>
///         This class lives directly in <c>Ninlil.Dns.Server.Authoritative</c>, not a
///         separate project the way <c>CaaRecordReconciliationService</c> lives in
///         <c>Server.Acme</c> -- unlike that class, <see cref="PtrSyncOptions" /> has no
///         cross-project placement constraint at all (it depends on nothing outside
///         <c>Server.Authoritative</c>/<c>Data.Abstractions</c> already available here), so there
///         is no circular-project-reference risk to design around.
///     </para>
/// </summary>
/// <param name="zoneRepository">The repository used to look up and create zones.</param>
/// <param name="recordRepository">This service's own record lookups run against this repository.</param>
/// <param name="zoneUpdateService">The service every add/remove is actually applied through.</param>
/// <param name="unitOfWork">The unit of work a newly auto-created reverse zone's own metadata is committed through.</param>
/// <param name="options">
///     The global default settings this service falls back to when a zone has not overridden
///     them, read fresh (<see cref="IOptionsMonitor{TOptions}.CurrentValue" />) on every call
///     rather than snapshotted once -- this service is registered scoped, but the plain
///     <see cref="IOptions{TOptions}" /> a scoped registration would otherwise receive is itself
///     backed by a singleton whose <c>.Value</c> is computed once and cached for the life of the
///     process, so a configuration-file write would never have been observed at all without this;
///     the same <see cref="IOptions{TOptions}" />-vs-<see cref="IOptionsMonitor{TOptions}" />
///     staleness trap several other hot-reloadable options classes in this codebase guard against
///     the same way.
/// </param>
/// <param name="logger">The logger this service records skip/reconcile decisions to.</param>
public class PtrSyncService(
    IZoneRepository zoneRepository,
    IRecordRepository recordRepository,
    IZoneUpdateService zoneUpdateService,
    IUnitOfWork unitOfWork,
    IOptionsMonitor<PtrSyncOptions> options,
    ILogger<PtrSyncService> logger
)
{
    /// <summary>
    ///     This constant contains the time-to-live, in seconds, every PTR/delegation-CNAME record
    ///     this service creates is given, matching this project's own default TTL for most other
    ///     infrequently-changing record types.
    /// </summary>
    private const uint PtrRecordTimeToLiveSeconds = 3600;

    private const uint DefaultSoaRefreshSeconds = 3600;
    private const uint DefaultSoaRetrySeconds = 900;
    private const uint DefaultSoaExpireSeconds = 604800;
    private const uint DefaultSoaMinimumTtlSeconds = 3600;

    /// <summary>
    ///     This method determines whether a zone's own <see cref="ZoneEntity.Type" /> is one this
    ///     feature ever syncs PTR records for at all -- <see cref="ZoneTypeEnum.Primary" /> or
    ///     <see cref="ZoneTypeEnum.CloudPrimary" />, the two zone types this server is itself the
    ///     source of truth for. Also used, unchanged, to decide whether an *existing* zone found
    ///     at a computed reverse zone name is safe to reuse (a <c>Primary</c>/<c>CloudPrimary</c>
    ///     zone this server already owns the content of) or must be left alone (any other zone
    ///     type -- delegated elsewhere, or otherwise not this server's own to write into).
    ///     Deliberately implemented as its own explicit, named, single-purpose check, exactly
    ///     mirroring <c>CaaRecordReconciliationService.IsEligibleZoneType</c> -- a
    ///     <c>Primary or Secondary</c> condition written inline at each call site is easy to leave
    ///     behind when a new zone type is added, which is the mistake this method prevents.
    /// </summary>
    /// <param name="zoneType">The zone type to check.</param>
    /// <returns>
    ///     <see langword="true" /> if <paramref name="zoneType" /> is eligible, <see langword="false" />
    ///     otherwise.
    /// </returns>
    public static bool IsEligibleZoneType(ZoneTypeEnum zoneType) =>
        zoneType == ZoneTypeEnum.Primary || zoneType == ZoneTypeEnum.CloudPrimary;

    /// <summary>
    ///     This method reconciles every reverse zone (and, for a classless IPv4 grouping, every
    ///     delegating parent zone) a single forward zone's own current A/AAAA record set implies --
    ///     the entry point both <see cref="PtrSyncEventSubscriber" /> (in response to
    ///     <c>DnsZoneChangedEvent</c>, discarding the returned result) and
    ///     <c>Ninlil.Web</c>'s own <c>ZonesController.ReconcilePtr</c> (an on-demand
    ///     <c>POST /api/zones/{id}/reconcile-ptr</c>, reporting the result to the caller) use.
    /// </summary>
    /// <param name="forwardZoneId">The identifier of the forward zone to sync PTR records from.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>What this pass actually did, or why it did nothing at all.</returns>
    public async Task<PtrReconciliationResult> ReconcileZoneAsync(
        Guid forwardZoneId,
        CancellationToken stoppingToken = default
    )
    {
        ZoneEntity forwardZone = await zoneRepository.GetByIdAsync(forwardZoneId, stoppingToken);

        if (forwardZone is null)
            return new PtrReconciliationResult(0, 0, 0, true, "Zone not found.");

        if (!IsEligibleZoneType(forwardZone.Type))
            return new PtrReconciliationResult(
                0,
                0,
                0,
                true,
                $"Zone type '{forwardZone.Type}' is not eligible for PTR sync -- only Primary/CloudPrimary zones are."
            );

        bool enabled = forwardZone.AutoManagePtrRecords ?? options.CurrentValue.DefaultEnabled;

        if (!enabled)
            return new PtrReconciliationResult(
                0,
                0,
                0,
                true,
                "PTR sync is not enabled for this zone, and the global default is currently off."
            );

        int ipv4PrefixBits =
            forwardZone.PtrIpv4PrefixLength
            ?? options.CurrentValue.DefaultIpv4ReverseZonePrefixBits;
        int ipv6PrefixBits =
            forwardZone.PtrIpv6PrefixLength
            ?? options.CurrentValue.DefaultIpv6ReverseZonePrefixBits;

        IReadOnlyList<RecordEntity> forwardRecords = await recordRepository.ListByZoneIdAsync(
            forwardZoneId,
            stoppingToken
        );

        // The desired PTR entries this forward zone's own current, enabled A/AAAA records imply,
        // grouped by the reverse zone name each one belongs to -- a single forward zone can span
        // more than one reverse zone if its own records span more than one grouping prefix. A
        // classless (>24-bit) IPv4 grouping additionally implies a desired RFC 2317 §4 delegation
        // CNAME in the octet-aligned /24 parent zone, grouped the same way.
        Dictionary<
            string,
            List<(string OwnerName, string TargetName)>
        > desiredPtrByReverseZoneName = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<
            string,
            List<(string OwnerName, string TargetName)>
        > desiredCnamesByParentZoneName = new(StringComparer.OrdinalIgnoreCase);

        foreach (RecordEntity record in forwardRecords)
        {
            if (record.IsDisabled)
                continue;

            if (record.Type is not (DnsResourceRecordTypeEnum.A or DnsResourceRecordTypeEnum.Aaaa))
                continue;

            IPAddress address = RecordEntityCodec.DecodeTypedData(record) switch
            {
                ARecordData a => a.Address,
                AaaaRecordData aaaa => aaaa.Address,
                _ => null,
            };

            if (address is null)
                continue;

            ReverseZoneComputation computation = ReverseZoneNameCalculator.Compute(
                address,
                ipv4PrefixBits,
                ipv6PrefixBits
            );

            AddEntry(
                desiredPtrByReverseZoneName,
                computation.ZoneName,
                computation.OwnerName,
                record.Name.TrimEnd('.')
            );

            if (computation.DelegationParentZoneName is not null)
                AddEntry(
                    desiredCnamesByParentZoneName,
                    computation.DelegationParentZoneName,
                    computation.DelegationParentOwnerName,
                    computation.OwnerName
                );
        }

        // Every PTR record anywhere that this feature previously created on this forward zone's
        // behalf -- found by its own decoded target belonging to this forward zone, not by which
        // reverse zone happens to hold it, since a reverse zone can in principle hold managed PTR
        // records contributed by more than one forward zone. This is what lets a reverse zone
        // that _used to_ have desired entries but no longer does (every A/AAAA record for that
        // prefix was removed, disabled, or changed) still get its now-stale managed records
        // cleaned up, even though it no longer appears in desiredPtrByReverseZoneName at all this
        // pass.
        IReadOnlyList<RecordEntity> allManagedPtrRecords = await recordRepository.ListAsync(
            record =>
                record.Type == DnsResourceRecordTypeEnum.Ptr
                && record.ManagedByFeature == RecordManagedByFeatureEnum.Ptr,
            stoppingToken
        );

        List<RecordEntity> existingManagedPtrForThisForwardZone =
        [
            .. allManagedPtrRecords.Where(record =>
                TargetBelongsToForwardZone(record, forwardZone.Name)
            ),
        ];

        // A managed delegation CNAME's own target is a reverse-zone owner name, not this forward
        // zone's own name -- so "belongs to this forward zone" is determined instead by whether
        // its target matches a PTR owner name this forward zone either currently desires this
        // pass, or has previously had this service manage on its behalf (covering the case where
        // the forward zone's own grouping granularity just changed, and a stale classless PTR
        // record -- and its stale delegation CNAME alongside it -- both need cleaning up).
        HashSet<string> childOwnerNamesRelevantToThisForwardZone = new(
            StringComparer.OrdinalIgnoreCase
        );
        foreach (RecordEntity ptrRecord in existingManagedPtrForThisForwardZone)
            childOwnerNamesRelevantToThisForwardZone.Add(NormalizeName(ptrRecord.Name));
        foreach (
            List<(
                string OwnerName,
                string TargetName
            )> entries in desiredPtrByReverseZoneName.Values
        )
        foreach ((string ownerName, _) in entries)
            childOwnerNamesRelevantToThisForwardZone.Add(NormalizeName(ownerName));

        IReadOnlyList<RecordEntity> allManagedDelegationCnames = await recordRepository.ListAsync(
            record =>
                record.Type == DnsResourceRecordTypeEnum.Cname
                && record.ManagedByFeature == RecordManagedByFeatureEnum.Ptr,
            stoppingToken
        );

        List<RecordEntity> existingManagedCnamesForThisForwardZone =
        [
            .. allManagedDelegationCnames.Where(record =>
                childOwnerNamesRelevantToThisForwardZone.Contains(
                    NormalizeName(DecodeTarget(record))
                )
            ),
        ];

        int totalAdded = 0;
        int totalRemoved = 0;
        HashSet<Guid> touchedZoneIds = [];

        // Reconcile every reverse zone's own PTR records first, so a classless child zone's real
        // PTR data exists before its delegating parent CNAME is written alongside it.
        HashSet<Guid> reconciledPtrZoneIds = [];
        foreach (
            (
                string reverseZoneName,
                List<(string OwnerName, string TargetName)> desiredEntries
            ) in desiredPtrByReverseZoneName
        )
        {
            (Guid? zoneId, int added, int removed) = await ReconcileZoneRecordEntriesAsync(
                reverseZoneName,
                null,
                desiredEntries,
                existingManagedPtrForThisForwardZone,
                BuildPtrRecord,
                "PTR",
                stoppingToken
            );

            if (zoneId is { } id)
            {
                reconciledPtrZoneIds.Add(id);
                touchedZoneIds.Add(id);
            }

            totalAdded += added;
            totalRemoved += removed;
        }

        foreach (
            Guid staleReverseZoneId in existingManagedPtrForThisForwardZone
                .Select(record => record.ZoneId)
                .Distinct()
                .Where(zoneId => !reconciledPtrZoneIds.Contains(zoneId))
        )
        {
            (Guid? zoneId, int added, int removed) = await ReconcileZoneRecordEntriesAsync(
                null,
                staleReverseZoneId,
                [],
                existingManagedPtrForThisForwardZone,
                BuildPtrRecord,
                "PTR",
                stoppingToken
            );

            if (zoneId is { } id)
                touchedZoneIds.Add(id);

            totalAdded += added;
            totalRemoved += removed;
        }

        // Then reconcile every classless delegation CNAME, in each delegating parent zone --
        // symmetric to the PTR reconciliation immediately above, including its own stale-zone
        // cleanup for a parent zone that no longer has any desired delegation CNAME this pass
        // (the forward zone's own grouping granularity changed back to the octet-aligned default,
        // or every address in that classless block was removed).
        HashSet<Guid> reconciledCnameZoneIds = [];
        foreach (
            (
                string parentZoneName,
                List<(string OwnerName, string TargetName)> desiredEntries
            ) in desiredCnamesByParentZoneName
        )
        {
            (Guid? zoneId, int added, int removed) = await ReconcileZoneRecordEntriesAsync(
                parentZoneName,
                null,
                desiredEntries,
                existingManagedCnamesForThisForwardZone,
                BuildDelegationCnameRecord,
                "RFC 2317 delegation CNAME",
                stoppingToken
            );

            if (zoneId is { } id)
            {
                reconciledCnameZoneIds.Add(id);
                touchedZoneIds.Add(id);
            }

            totalAdded += added;
            totalRemoved += removed;
        }

        foreach (
            Guid staleParentZoneId in existingManagedCnamesForThisForwardZone
                .Select(record => record.ZoneId)
                .Distinct()
                .Where(zoneId => !reconciledCnameZoneIds.Contains(zoneId))
        )
        {
            (Guid? zoneId, int added, int removed) = await ReconcileZoneRecordEntriesAsync(
                null,
                staleParentZoneId,
                [],
                existingManagedCnamesForThisForwardZone,
                BuildDelegationCnameRecord,
                "RFC 2317 delegation CNAME",
                stoppingToken
            );

            if (zoneId is { } id)
                touchedZoneIds.Add(id);

            totalAdded += added;
            totalRemoved += removed;
        }

        return new PtrReconciliationResult(
            totalAdded,
            totalRemoved,
            touchedZoneIds.Count,
            false,
            null
        );
    }

    /// <summary>
    ///     This method adds a single desired entry to a reverse/parent-zone-name-keyed map,
    ///     creating that zone name's own entry list the first time it is needed.
    /// </summary>
    private static void AddEntry(
        Dictionary<string, List<(string OwnerName, string TargetName)>> entriesByZoneName,
        string zoneName,
        string ownerName,
        string targetName
    )
    {
        if (
            !entriesByZoneName.TryGetValue(
                zoneName,
                out List<(string OwnerName, string TargetName)> entries
            )
        )
            entriesByZoneName[zoneName] = entries = [];

        entries.Add((ownerName, targetName));
    }

    /// <summary>
    ///     This method determines whether a stored PTR record's own decoded target belongs to a
    ///     forward zone -- either the zone's own apex, or a name underneath it.
    /// </summary>
    private static bool TargetBelongsToForwardZone(RecordEntity ptrRecord, string forwardZoneName)
    {
        string target = DecodeTarget(ptrRecord);
        if (target.Length == 0)
            return false;

        return string.Equals(target, forwardZoneName, StringComparison.OrdinalIgnoreCase)
            || target.EndsWith($".{forwardZoneName}", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     This method does the actual work for a single reverse or delegating-parent zone:
    ///     finding or auto-creating it (skipping, log-only, if a zone already exists at that name
    ///     but is not one this server owns the content of), diffing its own currently-managed
    ///     records (PTR or delegation CNAME, per <paramref name="buildRecord" />) against the
    ///     desired set, and applying the difference. Shared between this class's own PTR
    ///     reconciliation and its RFC 2317 delegation-CNAME reconciliation -- the two are
    ///     otherwise structurally identical (auto-create, diff, apply, tolerate a not-yet-signed
    ///     zone), differing only in which record type/builder they work with.
    /// </summary>
    /// <param name="zoneName">
    ///     The zone's own name, or <see langword="null" /> when <paramref name="explicitZoneId" />
    ///     is supplied instead (the "clean up a now-stale zone" pass, which already knows the
    ///     zone's id from a previously-managed record and has no fresh desired entries to compute
    ///     a name from).
    /// </param>
    /// <param name="explicitZoneId">The zone's own identifier, when already known, in place of looking it up by <paramref name="zoneName" />.</param>
    /// <param name="desiredEntries">The entries this zone should contain for this forward zone.</param>
    /// <param name="existingManagedCandidates">
    ///     Every record, in any zone, of this record type that this feature previously created on
    ///     this forward zone's behalf -- filtered down to this specific zone inside this method.
    /// </param>
    /// <param name="buildRecord">Builds a new record of the right type for a single desired entry.</param>
    /// <param name="recordKindForLogging">A short, human-readable label for this record kind, used only in log messages.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The zone's own identifier (or <see langword="null" /> if skipped), plus how many records were added/removed.</returns>
    private async Task<(Guid? ZoneId, int Added, int Removed)> ReconcileZoneRecordEntriesAsync(
        string zoneName,
        Guid? explicitZoneId,
        List<(string OwnerName, string TargetName)> desiredEntries,
        List<RecordEntity> existingManagedCandidates,
        Func<Guid, (string OwnerName, string TargetName), RecordEntity> buildRecord,
        string recordKindForLogging,
        CancellationToken stoppingToken
    )
    {
        ZoneEntity zone = explicitZoneId is { } explicitId
            ? await zoneRepository.GetByIdAsync(explicitId, stoppingToken)
            : await zoneRepository.FindByNameAsync(zoneName, stoppingToken);

        if (zone is null)
        {
            // Nothing to create for zero desired entries -- this only happens for the
            // "clean up a stale zone" pass finding the zone itself already gone (an operator
            // deleted it directly), which needs no action at all.
            if (desiredEntries.Count == 0)
                return (null, 0, 0);

            zone = await CreateReverseZoneAsync(zoneName, stoppingToken);

            List<RecordEntity> initialRecords =
            [
                BuildDefaultSoaRecord(zone),
                .. desiredEntries.Select(entry => buildRecord(zone.Id, entry)),
            ];

            await zoneUpdateService.BootstrapZoneAsync(zone.Id, initialRecords, stoppingToken);

            logger.LogInformation(
                "Auto-created zone '{ZoneName}' and populated it with {Count} {RecordKind} record(s).",
                zone.Name,
                desiredEntries.Count,
                recordKindForLogging
            );

            return (zone.Id, desiredEntries.Count, 0);
        }

        if (!IsEligibleZoneType(zone.Type))
        {
            // The zone this address's record belongs in already exists locally, but under a zone
            // type this server does not own the content of (Secondary/CloudSecondary/Stub/
            // Forwarder/...) -- it is delegated or synced from elsewhere. Never create a
            // conflicting second copy, and never write into it.
            logger.LogInformation(
                "Zone '{ZoneName}' already exists locally as {ZoneType}; skipping {RecordKind} sync "
                    + "for it rather than creating a conflicting second copy or writing into a zone "
                    + "this server does not own the content of.",
                zone.Name,
                zone.Type,
                recordKindForLogging
            );
            return (null, 0, 0);
        }

        List<RecordEntity> existingManagedHere =
        [
            .. existingManagedCandidates.Where(record => record.ZoneId == zone.Id),
        ];

        HashSet<(string Owner, string Target)> desiredKeys =
        [
            .. desiredEntries.Select(entry => KeyOf(entry.OwnerName, entry.TargetName)),
        ];
        HashSet<(string Owner, string Target)> existingKeys =
        [
            .. existingManagedHere.Select(record => KeyOf(record.Name, DecodeTarget(record))),
        ];

        List<RecordEntity> recordsToRemove =
        [
            .. existingManagedHere.Where(record =>
                !desiredKeys.Contains(KeyOf(record.Name, DecodeTarget(record)))
            ),
        ];
        List<RecordEntity> recordsToAdd =
        [
            .. desiredEntries
                .Where(entry => !existingKeys.Contains(KeyOf(entry.OwnerName, entry.TargetName)))
                .Select(entry => buildRecord(zone.Id, entry)),
        ];

        if (recordsToAdd.Count == 0 && recordsToRemove.Count == 0)
            return (zone.Id, 0, 0);

        try
        {
            await zoneUpdateService.ApplyChangesAsync(
                zone.Id,
                recordsToAdd,
                recordsToRemove,
                null,
                stoppingToken
            );

            logger.LogInformation(
                "Synced {RecordKind} records for zone '{ZoneName}': added {Added}, removed {Removed}.",
                recordKindForLogging,
                zone.Name,
                recordsToAdd.Count,
                recordsToRemove.Count
            );
        }
        catch (InvalidOperationException exception)
        {
            // A zone with no SOA record yet cannot have its serial bumped -- the zone's own
            // future bootstrap re-fires DnsZoneChangedEvent and gives this service another
            // chance.
            logger.LogDebug(
                exception,
                "Could not sync {RecordKind} records for zone '{ZoneName}' yet -- it may not have "
                    + "an SOA record to advance.",
                recordKindForLogging,
                zone.Name
            );
            return (zone.Id, 0, 0);
        }

        return (zone.Id, recordsToAdd.Count, recordsToRemove.Count);
    }

    /// <summary>
    ///     This method builds the case-insensitive owner/target key a managed record and a
    ///     desired entry are matched by.
    /// </summary>
    private static (string Owner, string Target) KeyOf(string ownerName, string targetName) =>
        (NormalizeName(ownerName), NormalizeName(targetName));

    /// <summary>
    ///     This method lowercases and trims the trailing dot from a DNS name, for
    ///     case-/trailing-dot-insensitive comparison.
    /// </summary>
    private static string NormalizeName(string name) => name.TrimEnd('.').ToLowerInvariant();

    /// <summary>
    ///     This method decodes a stored PTR or CNAME record's own target name, as a
    ///     presentation-format string, or an empty string if it cannot be decoded (never matches
    ///     any desired entry, so a record like this is always treated as no-longer-desired and
    ///     removed). Both <see cref="PtrRecordData" /> and <see cref="CnameRecordData" /> wrap the
    ///     same single <c>DnsName Target</c> shape, so one decode method covers both of this
    ///     service's own managed record types.
    /// </summary>
    private static string DecodeTarget(RecordEntity record) =>
        RecordEntityCodec.DecodeTypedData(record) switch
        {
            PtrRecordData ptr => ptr.Target.ToString().TrimEnd('.'),
            CnameRecordData cname => cname.Target.ToString().TrimEnd('.'),
            _ => string.Empty,
        };

    /// <summary>
    ///     This method creates a brand-new reverse/delegating-parent zone's own metadata shell --
    ///     as <see cref="ZoneTypeEnum.Primary" />, matching this server's own role for a zone it
    ///     is originating PTR/delegation data for -- committing it immediately so
    ///     <see cref="IZoneUpdateService.BootstrapZoneAsync" /> can find it by id right after.
    /// </summary>
    private async Task<ZoneEntity> CreateReverseZoneAsync(
        string reverseZoneName,
        CancellationToken stoppingToken
    )
    {
        ZoneEntity zone = new()
        {
            Id = Guid.NewGuid(),
            Name = reverseZoneName,
            Type = ZoneTypeEnum.Primary,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        await zoneRepository.AddAsync(zone, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return zone;
    }

    /// <summary>
    ///     This method builds a brand-new reverse/delegating-parent zone's own first SOA record.
    ///     No setting anywhere in this project names a "default nameserver hostname" this could
    ///     reuse (a forward zone's own SOA is always operator-supplied, through
    ///     <c>RecordsController</c>), so this generates a minimal, self-referential placeholder
    ///     instead -- naming the zone's own apex as its primary name server and a generic
    ///     <c>hostmaster</c> mailbox at that same apex -- which an operator can edit afterward
    ///     through the ordinary <c>RecordsController</c> PUT/PATCH endpoints exactly like any
    ///     other record, the same way <c>ZonesController.Create</c> leaves a REST-created zone
    ///     with no records at all for its own caller to fill in.
    /// </summary>
    private static RecordEntity BuildDefaultSoaRecord(ZoneEntity zone) =>
        new()
        {
            Id = Guid.NewGuid(),
            ZoneId = zone.Id,
            Name = zone.Name,
            Type = DnsResourceRecordTypeEnum.Soa,
            Class = DnsClassEnum.Internet,
            TimeToLiveSeconds = PtrRecordTimeToLiveSeconds,
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
    ///     This method builds a single desired PTR record, marked with this feature's own
    ///     provenance marker.
    /// </summary>
    private static RecordEntity BuildPtrRecord(
        Guid reverseZoneId,
        (string OwnerName, string TargetName) entry
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            ZoneId = reverseZoneId,
            Name = entry.OwnerName,
            Type = DnsResourceRecordTypeEnum.Ptr,
            Class = DnsClassEnum.Internet,
            TimeToLiveSeconds = PtrRecordTimeToLiveSeconds,
            DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                typeof(PtrRecordData),
                new PtrRecordData(DnsName.Parse(entry.TargetName))
            ),
            ManagedByFeature = RecordManagedByFeatureEnum.Ptr,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

    /// <summary>
    ///     This method builds a single desired RFC 2317 §4 delegation CNAME, in the octet-aligned
    ///     <c>/24</c> parent zone, pointing at the classless child zone's own owner name for the
    ///     same address -- marked with this same feature's own provenance marker (see this
    ///     class's own doc comment for why a CNAME reuses <see cref="RecordManagedByFeatureEnum.Ptr" />
    ///     rather than a dedicated enum value).
    /// </summary>
    private static RecordEntity BuildDelegationCnameRecord(
        Guid parentZoneId,
        (string OwnerName, string TargetName) entry
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            ZoneId = parentZoneId,
            Name = entry.OwnerName,
            Type = DnsResourceRecordTypeEnum.Cname,
            Class = DnsClassEnum.Internet,
            TimeToLiveSeconds = PtrRecordTimeToLiveSeconds,
            DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                typeof(CnameRecordData),
                new CnameRecordData(DnsName.Parse(entry.TargetName))
            ),
            ManagedByFeature = RecordManagedByFeatureEnum.Ptr,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
}
