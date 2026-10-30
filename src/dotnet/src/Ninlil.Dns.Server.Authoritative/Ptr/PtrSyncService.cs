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
///     This class does PTR sync: from a <see cref="ZoneTypeEnum.Primary" /> or
///     <see cref="ZoneTypeEnum.CloudPrimary" /> forward zone's enabled A/AAAA records it computes
///     the PTR records they imply, creates the reverse zone when needed (never a second copy of an
///     existing one), and makes the reverse zone's managed PTR records match. It only touches
///     records it created (<see cref="RecordEntity.ManagedByFeature" /> ==
///     <see cref="RecordManagedByFeatureEnum.Ptr" />), never operator-added ones. Changes go through
///     <see cref="IZoneUpdateService" />.
///     <para>
///     When the zone's IPv4 prefix length (<see cref="ZoneEntity.PtrIpv4PrefixLength" />, else
///     <see cref="PtrSyncOptions.DefaultIpv4ReverseZonePrefixBits" />) is longer than 24, it also
///     maintains the RFC 2317 §4 CNAMEs: the /24 zone gets a CNAME, at the name an ordinary PTR
///     would have, pointing to the PTR in the classless zone. These CNAMEs also carry
///     <see cref="RecordManagedByFeatureEnum.Ptr" />; the record type tells them apart from PTRs.
///     </para>
///     <para>
///     It lives in this project, unlike <c>CaaRecordReconciliationService</c> in
///     <c>Server.Acme</c>, because it needs nothing from outside <c>Server.Authoritative</c> and
///     <c>Data.Abstractions</c>.
///     </para>
/// </summary>
/// <param name="zoneRepository">The repository used to look up and create zones.</param>
/// <param name="recordRepository">The repository records are read from.</param>
/// <param name="zoneUpdateService">The service every add and remove goes through.</param>
/// <param name="unitOfWork">The unit of work a newly created reverse zone is saved through.</param>
/// <param name="options">
///     The global defaults used when a zone sets no value, read through
///     <see cref="IOptionsMonitor{TOptions}.CurrentValue" /> on every call.
///     <see cref="IOptions{TOptions}" /> would keep the startup value, even in a scoped service.
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
    ///     This constant contains the TTL, in seconds, of every PTR and delegation CNAME this service
    ///     creates.
    /// </summary>
    private const uint PtrRecordTimeToLiveSeconds = 3600;

    /// <summary>
    ///     This field serializes the passes of this process. A pass reads the managed records, works
    ///     out the difference and writes it, so two passes that overlap both see the same missing
    ///     records and both add them; the database has no unique key on records that would refuse the
    ///     second copy. The event-driven pass and <c>POST /api/zones/{id}/reconcile-ptr</c>
    ///     run on separate scopes, so a per-instance lock cannot cover them. One lock for all zones
    ///     is enough: a pass only needs the database for a few commands.
    /// </summary>
    private static readonly SemaphoreSlim passLock = new(1, 1);

    private const uint DefaultSoaRefreshSeconds = 3600;
    private const uint DefaultSoaRetrySeconds = 900;
    private const uint DefaultSoaExpireSeconds = 604800;
    private const uint DefaultSoaMinimumTtlSeconds = 3600;

    /// <summary>
    ///     This method reports whether a zone type is eligible: <see cref="ZoneTypeEnum.Primary" /> or
    ///     <see cref="ZoneTypeEnum.CloudPrimary" />, the types this server is the source for. It
    ///     decides both which forward zones are synced and whether an existing zone at a reverse zone
    ///     name may be written to. Keeping it in one method, as
    ///     <c>CaaRecordReconciliationService.IsEligibleZoneType</c> does, avoids missing a call site
    ///     when zone types are added.
    /// </summary>
    /// <param name="zoneType">The zone type to check.</param>
    /// <returns>
    ///     Whether <paramref name="zoneType" /> is eligible.
    /// </returns>
    public static bool IsEligibleZoneType(ZoneTypeEnum zoneType) =>
        zoneType == ZoneTypeEnum.Primary || zoneType == ZoneTypeEnum.CloudPrimary;

    /// <summary>
    ///     This method reconciles every reverse zone (and, for classless IPv4, every /24 zone
    ///     carrying CNAMEs) implied by one forward zone's A/AAAA records.
    ///     <see cref="PtrSyncEventSubscriber" /> calls it on zone changes and discards the result;
    ///     <c>ZonesController.ReconcilePtr</c> (<c>POST /api/zones/{id}/reconcile-ptr</c>) returns it.
    /// </summary>
    /// <param name="forwardZoneId">The identifier of the forward zone to sync PTR records from.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>What the pass did, or why it did nothing.</returns>
    public async Task<PtrReconciliationResult> ReconcileZoneAsync(
        Guid forwardZoneId,
        CancellationToken stoppingToken = default
    )
    {
        await passLock.WaitAsync(stoppingToken);
        try
        {
            return await ReconcileZoneUnlockedAsync(forwardZoneId, stoppingToken);
        }
        finally
        {
            passLock.Release();
        }
    }

    /// <summary>
    ///     This method is one pass of <see cref="ReconcileZoneAsync" />; the caller holds
    ///     <see cref="passLock" />.
    /// </summary>
    private async Task<PtrReconciliationResult> ReconcileZoneUnlockedAsync(
        Guid forwardZoneId,
        CancellationToken stoppingToken
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

        // The PTR entries the forward zone's enabled A/AAAA records imply, grouped by reverse zone
        // (one forward zone can span several). Classless IPv4 also implies RFC 2317 CNAMEs in the /24
        // zone, grouped the same way.
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

        // Every managed PTR whose target is in this forward zone, wherever it is stored (a reverse
        // zone can hold PTRs for several forward zones). This finds stale PTRs in reverse zones that
        // no longer have any desired entries.
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

        // A managed CNAME's target is a reverse zone name, so it belongs to this forward zone if its
        // target is a PTR name this zone wants now or had managed before (which covers a change of
        // prefix length leaving a stale classless PTR and CNAME).
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

        // Reconcile PTRs first, so a classless zone's PTR exists before the /24 CNAME pointing to
        // it.
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

        // Then reconcile the CNAMEs in each /24 zone, the same way, including clean-up in zones
        // that no longer need any (the prefix went back to 24, or the block's addresses were
        // removed).
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
    ///     This method adds a desired entry to a map keyed by zone name.
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
    ///     This method checks whether a PTR's target is the forward zone's apex or below it.
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
    ///     This method reconciles one reverse or /24 zone: it finds or creates the zone (skipping,
    ///     with a log entry, if a zone of another type exists at that name), compares its managed
    ///     records (PTR or CNAME, per <paramref name="buildRecord" />) with the desired set, and applies
    ///     the difference. PTR and CNAME reconciliation share it.
    /// </summary>
    /// <param name="zoneName">
    ///     The zone's name, or <see langword="null" /> when <paramref name="explicitZoneId" /> is given
    ///     instead (the clean-up pass for a zone found through a previously managed record).
    /// </param>
    /// <param name="explicitZoneId">The zone's ID when already known, instead of looking it up by <paramref name="zoneName" />.</param>
    /// <param name="desiredEntries">The entries this zone should contain for this forward zone.</param>
    /// <param name="existingManagedCandidates">
    ///     Every managed record of this type for this forward zone, in any zone; filtered to this
    ///     zone here.
    /// </param>
    /// <param name="buildRecord">Builds a new record of the right type for a single desired entry.</param>
    /// <param name="recordKindForLogging">A short label for the record kind, for log messages.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The zone's ID (or <see langword="null" /> if skipped) and how many records were added and removed.</returns>
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
            // No desired entries and no zone: the clean-up pass found the zone already deleted, so
            // there is nothing to do.
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
            // A zone of another type (Secondary, CloudSecondary, Stub, Forwarder, ...) exists at this
            // name; its content comes from elsewhere, so do not write into it or create a second one.
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
            // A zone with no SOA yet cannot be changed; its bootstrap publishes DnsZoneChangedEvent and
            // triggers another pass.
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
    ///     This method builds the case-insensitive owner and target key used to match records with
    ///     desired entries.
    /// </summary>
    private static (string Owner, string Target) KeyOf(string ownerName, string targetName) =>
        (NormalizeName(ownerName), NormalizeName(targetName));

    /// <summary>
    ///     This method lowercases a name and trims the trailing dot, for comparison.
    /// </summary>
    private static string NormalizeName(string name) => name.TrimEnd('.').ToLowerInvariant();

    /// <summary>
    ///     This method decodes a stored PTR or CNAME record's target, or returns an empty string if it
    ///     cannot be decoded, so such a record never matches and is removed. Both record types hold a
    ///     single <c>DnsName Target</c>.
    /// </summary>
    private static string DecodeTarget(RecordEntity record) =>
        RecordEntityCodec.DecodeTypedData(record) switch
        {
            PtrRecordData ptr => ptr.Target.ToString().TrimEnd('.'),
            CnameRecordData cname => cname.Target.ToString().TrimEnd('.'),
            _ => string.Empty,
        };

    /// <summary>
    ///     This method creates a new reverse or /24 zone as <see cref="ZoneTypeEnum.Primary" /> and
    ///     saves it at once, so <see cref="IZoneUpdateService.BootstrapZoneAsync" /> can find it by ID.
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
    ///     This method builds the first SOA record for a new reverse or /24 zone. There is no default
    ///     name server setting, so it uses the zone apex as the primary name server and
    ///     <c>hostmaster</c> at the apex as the mailbox; the operator can edit it like any record. No
    ///     NS records are created.
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
                    DnsName.Parse(zone.Name).PrependLabels("hostmaster"),
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
    ///     This method builds one desired PTR record, marked as managed by PTR sync.
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
    ///     This method builds one RFC 2317 §4 CNAME in the /24 zone, pointing to the address's PTR
    ///     name in the classless zone, marked as managed by PTR sync (see the class summary).
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
