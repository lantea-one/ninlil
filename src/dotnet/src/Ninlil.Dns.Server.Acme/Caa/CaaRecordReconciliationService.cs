using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.Journal;

// Define our namespace.
namespace Ninlil.Dns.Server.Acme.Caa;

/// <summary>
///     This class reconciles CAA (RFC 8659) records. For a zone it works out the wanted CAA
///     record from the zone's issuer domain (or the global default) and makes the stored
///     records match, touching only records it created
///     (<see cref="RecordEntity.ManagedByFeature" /> == <see cref="RecordManagedByFeatureEnum.Caa" />),
///     never operator-added CAA records, and only in zones whose content this server owns.
///     <see cref="CaaRecordReconciliationEventSubscriber" />, <see cref="AcmeCertificateIssuer" />
///     and <c>ZonesController.ReconcileCaa</c> call it.
///     <para>
///         It lives in <c>Server.Acme</c>, not <c>Server.Authoritative</c>, although it mostly
///         uses the latter's <see cref="IZoneUpdateService" /> and repositories:
///         <c>Server.Acme</c> references <c>Server.Authoritative</c>, and placing it there would
///         need a reference back for <see cref="CaaAutoManagementOptions" />, a circular project
///         reference.
///     </para>
/// </summary>
/// <param name="zoneRepository">The repository used to look up the zone being reconciled.</param>
/// <param name="recordRepository">The repository the CAA records are looked up in.</param>
/// <param name="zoneUpdateService">The service every add and remove goes through, as for all zone content changes.</param>
/// <param name="options">
///     The global defaults used when a zone sets no value, read through
///     <see cref="IOptionsMonitor{TOptions}" /> so a change saved by
///     <c>AcmeSettingsController.Patch</c> applies to the next pass. <see cref="IOptions{TOptions}" />
///     would keep the first value for the process lifetime, even in a scoped service.
/// </param>
/// <param name="logger">The logger this service records skips and changes to.</param>
public class CaaRecordReconciliationService(
    IZoneRepository zoneRepository,
    IRecordRepository recordRepository,
    IZoneUpdateService zoneUpdateService,
    IOptionsMonitor<CaaAutoManagementOptions> options,
    ILogger<CaaRecordReconciliationService> logger
)
{
    /// <summary>
    ///     This constant contains the RFC 8659 CAA property tag this service writes, which names
    ///     the CA allowed to issue certificates for the name (and, with no <c>issuewild</c>
    ///     record, wildcard ones too). No <c>issuewild</c> or <c>iodef</c> record is generated; an
    ///     operator adds those as ordinary records.
    /// </summary>
    private const string IssueTag = "issue";

    /// <summary>
    ///     This constant contains the TTL, in seconds, of every CAA record this service creates.
    ///     CAs read CAA records only when issuing, so an ordinary one-hour TTL is fine.
    /// </summary>
    private const uint CaaRecordTimeToLiveSeconds = 3600;

    /// <summary>
    ///     This method decides whether CAA records are reconciled for a zone type: only
    ///     <see cref="ZoneTypeEnum.Primary" /> and <see cref="ZoneTypeEnum.CloudPrimary" />, whose
    ///     content this server owns. The rule is kept in one named method so that a new zone type
    ///     whose content this server owns has to be added here on purpose; a type check repeated
    ///     inline, like a zone-type filter in <see cref="AuthoritativeZoneCache" />, is easy to
    ///     miss when a type is added.
    /// </summary>
    /// <param name="zoneType">The zone type to check.</param>
    /// <returns>
    ///     <see langword="true" /> if <paramref name="zoneType" /> is eligible for CAA
    ///     reconciliation, otherwise <see langword="false" />, in particular for
    ///     <see cref="ZoneTypeEnum.Secondary" /> and <see cref="ZoneTypeEnum.CloudSecondary" />,
    ///     whose content comes from elsewhere and would be overwritten by the next transfer or
    ///     sync.
    /// </returns>
    public static bool IsEligibleZoneType(ZoneTypeEnum zoneType) =>
        zoneType == ZoneTypeEnum.Primary || zoneType == ZoneTypeEnum.CloudPrimary;

    /// <summary>
    ///     This method reconciles the CAA records of the zone with the given identifier.
    ///     <see cref="CaaRecordReconciliationEventSubscriber" /> and
    ///     <c>ZonesController.ReconcileCaa</c> use it.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone to reconcile.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>What the pass did, or why it did nothing.</returns>
    public async Task<CaaReconciliationResult> ReconcileZoneAsync(
        Guid zoneId,
        CancellationToken stoppingToken = default
    )
    {
        ZoneEntity zone = await zoneRepository.GetByIdAsync(zoneId, stoppingToken);

        if (zone is null)
            return new CaaReconciliationResult(0, 0, true, "Zone not found.");

        return await ReconcileZoneAsync(zone, stoppingToken);
    }

    /// <summary>
    ///     This method reconciles the CAA records of the zone whose name is
    ///     <paramref name="domainName" />. <see cref="AcmeCertificateIssuer" /> uses it after an
    ///     issuance, since <see cref="AcmeOptions.Domains" /> holds names, not zone identifiers.
    ///     The lookup is by exact zone name, so a domain below a zone apex is skipped as not
    ///     hosted.
    /// </summary>
    /// <param name="domainName">The domain name whose zone should be reconciled.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>What the pass did, or why it did nothing.</returns>
    public async Task<CaaReconciliationResult> ReconcileDomainAsync(
        string domainName,
        CancellationToken stoppingToken = default
    )
    {
        ZoneEntity zone = await zoneRepository.FindByNameAsync(domainName, stoppingToken);

        if (zone is null)
        {
            logger.LogDebug(
                "'{Domain}' is not a zone this server hosts; skipping CAA reconciliation for it.",
                domainName
            );
            return new CaaReconciliationResult(
                0,
                0,
                true,
                $"'{domainName}' is not a zone this server hosts."
            );
        }

        return await ReconcileZoneAsync(zone, stoppingToken);
    }

    /// <summary>
    ///     This method reconciles a loaded zone: it checks eligibility, resolves the issuer
    ///     domain, compares the wanted CAA record with the records this feature created at the
    ///     apex, and applies the difference through <see cref="IZoneUpdateService" />.
    /// </summary>
    private async Task<CaaReconciliationResult> ReconcileZoneAsync(
        ZoneEntity zone,
        CancellationToken stoppingToken
    )
    {
        if (!IsEligibleZoneType(zone.Type))
            return new CaaReconciliationResult(
                0,
                0,
                true,
                $"Zone type '{zone.Type}' is not eligible for CAA reconciliation -- only Primary/CloudPrimary zones are."
            );

        bool enabled = zone.AutoManageCaaRecords ?? options.CurrentValue.DefaultEnabled;

        if (!enabled)
            return new CaaReconciliationResult(
                0,
                0,
                true,
                "CAA auto-management is not enabled for this zone, and the global default is currently off."
            );

        string issuerDomain = string.IsNullOrWhiteSpace(zone.CaaIssuerDomain)
            ? options.CurrentValue.DefaultIssuerDomain
            : zone.CaaIssuerDomain;

        if (string.IsNullOrWhiteSpace(issuerDomain))
        {
            logger.LogDebug(
                "Zone '{ZoneName}' has CAA auto-management enabled but no issuer domain is "
                    + "resolved (neither its own CaaIssuerDomain nor "
                    + "CaaAutoManagementOptions.DefaultIssuerDomain is set); skipping "
                    + "reconciliation rather than writing a CAA record with an empty issuer.",
                zone.Name
            );
            return new CaaReconciliationResult(
                0,
                0,
                true,
                "CAA auto-management is enabled but no issuer domain is resolved (neither this "
                    + "zone's own issuer domain nor the global default is set)."
            );
        }

        IReadOnlyList<RecordEntity> existingRecordsAtApex = await recordRepository.FindByNameAsync(
            zone.Id,
            zone.Name,
            DnsResourceRecordTypeEnum.Caa,
            stoppingToken
        );

        // Only records this feature created are considered. An operator-added CAA record
        // (ManagedByFeature null) at the same name is never matched, removed or counted.
        List<RecordEntity> managedRecords =
        [
            .. existingRecordsAtApex.Where(record =>
                record.ManagedByFeature == RecordManagedByFeatureEnum.Caa
            ),
        ];

        RecordEntity matchingRecord = managedRecords.FirstOrDefault(record =>
            MatchesDesiredIssueRecord(record, issuerDomain)
        );

        List<RecordEntity> recordsToRemove =
        [
            .. managedRecords.Where(record => !ReferenceEquals(record, matchingRecord)),
        ];
        List<RecordEntity> recordsToAdd = matchingRecord is null
            ? [BuildDesiredIssueRecord(zone, issuerDomain)]
            : [];

        if (recordsToAdd.Count == 0 && recordsToRemove.Count == 0)
            return new CaaReconciliationResult(0, 0, false, null);

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
                "Reconciled CAA records for zone '{ZoneName}': added {Added}, removed {Removed}.",
                zone.Name,
                recordsToAdd.Count,
                recordsToRemove.Count
            );
        }
        catch (InvalidOperationException exception)
        {
            // A new zone with no SOA record yet cannot have its serial advanced (the state
            // BootstrapZoneAsync fills). The pass reports no change; the zone's next change
            // raises DnsZoneChangedEvent and reconciliation runs again.
            logger.LogDebug(
                exception,
                "Could not reconcile CAA records for zone '{ZoneName}' yet -- it may not have an "
                    + "SOA record to advance.",
                zone.Name
            );
            return new CaaReconciliationResult(0, 0, false, null);
        }

        return new CaaReconciliationResult(recordsToAdd.Count, recordsToRemove.Count, false, null);
    }

    /// <summary>
    ///     This method checks whether a managed record already holds the wanted policy, a
    ///     non-critical <c>issue</c> tag naming the issuer domain (compared ignoring case). Then
    ///     the pass writes nothing, which matters because a write raises
    ///     <see cref="Core.Events.DnsZoneChangedEvent" /> and would start the reconciliation
    ///     again.
    /// </summary>
    private static bool MatchesDesiredIssueRecord(RecordEntity record, string issuerDomain) =>
        RecordEntityCodec.DecodeTypedData(record) is CaaRecordData data
        && !data.IsCritical
        && string.Equals(data.Tag, IssueTag, StringComparison.OrdinalIgnoreCase)
        && string.Equals(data.Value, issuerDomain, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     This method builds the one CAA record this feature creates for a zone: a non-critical
    ///     <c>issue</c> tag at the zone apex naming the issuer domain.
    /// </summary>
    private static RecordEntity BuildDesiredIssueRecord(ZoneEntity zone, string issuerDomain) =>
        new()
        {
            Id = Guid.NewGuid(),
            ZoneId = zone.Id,
            Name = zone.Name,
            Type = DnsResourceRecordTypeEnum.Caa,
            Class = DnsClassEnum.Internet,
            TimeToLiveSeconds = CaaRecordTimeToLiveSeconds,
            DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                typeof(CaaRecordData),
                new CaaRecordData(false, IssueTag, issuerDomain)
            ),
            ManagedByFeature = RecordManagedByFeatureEnum.Caa,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
}
