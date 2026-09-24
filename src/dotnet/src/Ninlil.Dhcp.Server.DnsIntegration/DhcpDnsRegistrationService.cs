using System.Net;
using Microsoft.Extensions.Logging;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.Journal;

// Define our namespace.
namespace Ninlil.Dhcp.Server.DnsIntegration;

/// <summary>
///     This class is Ninlil's actual DHCP-&gt;DNS integration engine: on a lease grant, it
///     registers a real A (or, for a DHCPv6 lease, AAAA) record naming the client's own presented
///     host name against the address it currently leases, in whichever zone the owning
///     <see cref="DhcpScopeEntity.DnsZoneId" /> designates; on release or expiry, it removes that
///     same record again. Every write goes through the same <see cref="IZoneUpdateService" />
///     every other DNS-content-changing feature in this project already uses -- never a parallel,
///     bespoke DNS-writing path -- and touches only records this feature created itself
///     (<see cref="RecordEntity.ManagedByFeature" /> == <see cref="RecordManagedByFeatureEnum.Dhcp" />),
///     the same provenance-marker discipline <see cref="Ninlil.Dns.Server.Authoritative.Ptr.PtrSyncService" />/
///     <c>CaaRecordReconciliationService</c> already establish for their own automated writes.
///     <para>
///         <b>Reusing the existing PTR sync feature, rather than inventing a second
///         reverse-zone mechanism</b>: this class writes only the forward A/AAAA record. A PTR
///         record for that same address is a free side effect of the write above, not a second
///         write this class performs itself: <see cref="IZoneUpdateService.ApplyChangesAsync" />
///         already publishes <c>DnsZoneChangedEvent</c> on every real content change, and
///         <c>PtrSyncEventSubscriber</c> already reacts to that same event for every zone,
///         reconciling that zone's own implied PTR record set -- exactly the same event this
///         class's own write already triggers. No new trigger point, and no direct call from this
///         class into <c>PtrSyncService</c>, was needed at all.
///     </para>
///     <para>
///         <b>Why the designated zone is looked up per lease, not cached</b>: a lease event is
///         rare compared to an ordinary DNS query (at most once per DHCP transaction, not once per
///         resolution), so there is no hot-path performance reason to avoid a fresh repository
///         read here, and a fresh read is always correct even immediately after an operator
///         changes <see cref="DhcpScopeEntity.DnsZoneId" />.
///     </para>
/// </summary>
/// <param name="leaseRepository">The repository used to re-read the lease this event names.</param>
/// <param name="scopeRepository">The repository used to look up the lease's own owning scope.</param>
/// <param name="zoneRepository">The repository used to look up a scope's own designated DNS zone.</param>
/// <param name="recordRepository">The repository this service's own managed-record lookups run against.</param>
/// <param name="zoneUpdateService">The service every add/remove is actually applied through.</param>
/// <param name="logger">The logger this service records skip/registration decisions to.</param>
public class DhcpDnsRegistrationService(
    ILeaseRepository leaseRepository,
    IDhcpScopeRepository scopeRepository,
    IZoneRepository zoneRepository,
    IRecordRepository recordRepository,
    IZoneUpdateService zoneUpdateService,
    ILogger<DhcpDnsRegistrationService> logger
)
{
    /// <summary>
    ///     This constant contains the time-to-live, in seconds, every A/AAAA record this service
    ///     creates is given -- deliberately shorter than
    ///     <c>PtrSyncService</c>/<c>CaaRecordReconciliationService</c>'s own 3600-second default,
    ///     since a DHCP client's own address binding changes far more often than a CAA policy or
    ///     an ordinary PTR mapping, and a shorter TTL keeps a resolver from caching a departed
    ///     client's stale address for as long.
    /// </summary>
    private const uint DhcpRecordTimeToLiveSeconds = 300;

    /// <summary>
    ///     This method reacts to a single <c>DhcpLeaseChangedEvent</c>, re-reading the lease it
    ///     names fresh (the event itself is id-only) and registering, updating, or removing its
    ///     own A/AAAA record accordingly. A no-op whenever the owning scope has DHCP-&gt;DNS
    ///     integration disabled (<see cref="DhcpScopeEntity.DnsZoneId" /> unset), names a zone
    ///     this server does not itself own the content of, or the lease carries no usable host
    ///     name at all.
    /// </summary>
    /// <param name="leaseId">The identifier of the lease that changed.</param>
    /// <param name="changeKind">What actually happened to the lease.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>What this pass actually did, or why it did nothing at all.</returns>
    public async Task<DhcpDnsRegistrationResult> HandleLeaseChangeAsync(
        Guid leaseId,
        DhcpLeaseChangeKindEnum changeKind,
        CancellationToken stoppingToken = default
    )
    {
        LeaseEntity lease = await leaseRepository.GetByIdAsync(leaseId, stoppingToken);

        if (lease is null)
            return DhcpDnsRegistrationResult.Skipped("The lease no longer exists.");

        DhcpScopeEntity scope = await scopeRepository.FindByNameAsync(
            lease.ScopeName,
            stoppingToken
        );

        if (scope?.DnsZoneId is not { } dnsZoneId)
            return DhcpDnsRegistrationResult.Skipped(
                "The lease's own scope has no DNS zone designated for DHCP->DNS integration."
            );

        ZoneEntity zone = await zoneRepository.GetByIdAsync(dnsZoneId, stoppingToken);

        if (zone is null)
            return DhcpDnsRegistrationResult.Skipped(
                "The scope's own designated DNS zone no longer exists."
            );

        if (!IsEligibleZoneType(zone.Type))
            return DhcpDnsRegistrationResult.Skipped(
                $"Zone type '{zone.Type}' is not eligible for DHCP->DNS integration -- only Primary/CloudPrimary zones are."
            );

        DnsResourceRecordTypeEnum recordType =
            lease.ProtocolVersion == InternetProtocolVersionEnum.IPv6
                ? DnsResourceRecordTypeEnum.Aaaa
                : DnsResourceRecordTypeEnum.A;

        if (!IPAddress.TryParse(lease.IpAddress, out IPAddress leaseAddress))
            return DhcpDnsRegistrationResult.Skipped(
                $"The lease's own address ('{lease.IpAddress}') is not a valid IP address."
            );

        IReadOnlyList<RecordEntity> zoneRecords = await recordRepository.ListByZoneIdAsync(
            zone.Id,
            stoppingToken
        );
        List<RecordEntity> managedRecords =
        [
            .. zoneRecords.Where(record =>
                record.Type == recordType
                && record.ManagedByFeature == RecordManagedByFeatureEnum.Dhcp
            ),
        ];
        RecordEntity existingByAddress = managedRecords.FirstOrDefault(record =>
            AddressEquals(record, leaseAddress)
        );

        if (changeKind is DhcpLeaseChangeKindEnum.Released or DhcpLeaseChangeKindEnum.Expired)
        {
            if (existingByAddress is null)
                return DhcpDnsRegistrationResult.Skipped(
                    "No matching managed record exists for this lease's address."
                );

            await ApplyAsync(zone.Id, [], [existingByAddress], stoppingToken);
            return DhcpDnsRegistrationResult.Removed(existingByAddress.Name);
        }

        // DhcpLeaseChangeKindEnum.Granted from here on.
        string label = SanitizeHostNameLabel(lease.HostName);

        if (label is null)
            return DhcpDnsRegistrationResult.Skipped(
                "The lease carries no usable host name to register."
            );

        string ownerName = $"{label}.{zone.Name}";
        RecordEntity existingByName = managedRecords.FirstOrDefault(record =>
            !ReferenceEquals(record, existingByAddress)
            && NormalizeName(record.Name) == NormalizeName(ownerName)
        );

        if (
            existingByAddress is not null
            && existingByName is null
            && NormalizeName(existingByAddress.Name) == NormalizeName(ownerName)
        )
            // Already exactly correct -- no write at all, avoiding a pointless
            // DnsZoneChangedEvent republish that would otherwise re-trigger every other zone
            // change subscriber (most notably PtrSyncEventSubscriber) for nothing.
            return DhcpDnsRegistrationResult.Unchanged(ownerName);

        List<RecordEntity> recordsToRemove = [];
        if (existingByAddress is not null)
            recordsToRemove.Add(existingByAddress);
        if (existingByName is not null)
            recordsToRemove.Add(existingByName);

        RecordEntity newRecord = BuildRecord(zone.Id, ownerName, recordType, leaseAddress);
        await ApplyAsync(zone.Id, [newRecord], recordsToRemove, stoppingToken);

        return DhcpDnsRegistrationResult.Registered(ownerName);
    }

    /// <summary>
    ///     This method determines whether a zone's own <see cref="ZoneEntity.Type" /> is one this
    ///     feature ever registers records into at all -- <see cref="ZoneTypeEnum.Primary" /> or
    ///     <see cref="ZoneTypeEnum.CloudPrimary" />, the two zone types this server is itself the
    ///     source of truth for. The same explicit, named, single-purpose check
    ///     <c>PtrSyncService.IsEligibleZoneType</c>/<c>CaaRecordReconciliationService.IsEligibleZoneType</c>
    ///     already establish: a zone-type filter written inline at each call site is easy to leave
    ///     behind when a new answerable zone type is added, silently skipping that type.
    /// </summary>
    /// <param name="zoneType">The zone type to check.</param>
    /// <returns><see langword="true" /> if eligible, <see langword="false" /> otherwise.</returns>
    public static bool IsEligibleZoneType(ZoneTypeEnum zoneType) =>
        zoneType == ZoneTypeEnum.Primary || zoneType == ZoneTypeEnum.CloudPrimary;

    /// <summary>
    ///     This method applies a set of record additions/removals via <see cref="IZoneUpdateService" />,
    ///     tolerating the one real, expected failure -- a zone with no SOA record yet to advance
    ///     -- the same way <c>PtrSyncService</c>/<c>CaaRecordReconciliationService</c> already do:
    ///     there is simply nothing to do yet, and the zone's very next real content change gives
    ///     this feature another chance via a fresh <c>DnsZoneChangedEvent</c>.
    /// </summary>
    private async Task ApplyAsync(
        Guid zoneId,
        IReadOnlyList<RecordEntity> recordsToAdd,
        IReadOnlyList<RecordEntity> recordsToRemove,
        CancellationToken stoppingToken
    )
    {
        try
        {
            await zoneUpdateService.ApplyChangesAsync(
                zoneId,
                recordsToAdd,
                recordsToRemove,
                explicitNewSerial: null,
                stoppingToken
            );
        }
        catch (InvalidOperationException exception)
        {
            logger.LogDebug(
                exception,
                "Could not apply a DHCP->DNS registration change to zone {ZoneId} yet -- it may not have an SOA record to advance.",
                zoneId
            );
        }
    }

    /// <summary>
    ///     This method decodes a stored A/AAAA record's own address and compares it against a
    ///     lease's own current address, returning <see langword="false" /> (never matching) for a
    ///     record whose data cannot be decoded at all.
    /// </summary>
    private static bool AddressEquals(RecordEntity record, IPAddress leaseAddress) =>
        RecordEntityCodec.DecodeTypedData(record) switch
        {
            ARecordData a => a.Address.Equals(leaseAddress),
            AaaaRecordData aaaa => aaaa.Address.Equals(leaseAddress),
            _ => false,
        };

    /// <summary>
    ///     This method builds the single A/AAAA record this feature ever creates for a lease.
    /// </summary>
    private static RecordEntity BuildRecord(
        Guid zoneId,
        string ownerName,
        DnsResourceRecordTypeEnum recordType,
        IPAddress address
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            ZoneId = zoneId,
            Name = ownerName,
            Type = recordType,
            Class = DnsClassEnum.Internet,
            TimeToLiveSeconds = DhcpRecordTimeToLiveSeconds,
            DataJson =
                recordType == DnsResourceRecordTypeEnum.Aaaa
                    ? Ninlil.Common.Serializer.JsonSerializer.Serialize(
                        typeof(AaaaRecordData),
                        new AaaaRecordData(address)
                    )
                    : Ninlil.Common.Serializer.JsonSerializer.Serialize(
                        typeof(ARecordData),
                        new ARecordData(address)
                    ),
            ManagedByFeature = RecordManagedByFeatureEnum.Dhcp,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

    /// <summary>
    ///     This method normalizes an owner name for case/trailing-dot-insensitive comparison,
    ///     mirroring <c>PtrSyncService</c>'s own identically-named helper exactly.
    /// </summary>
    private static string NormalizeName(string name) => name.TrimEnd('.').ToLowerInvariant();

    /// <summary>
    ///     This method converts a DHCP client's own free-form presented host name (RFC 2132
    ///     Section 3.14's Host Name option) into a single, valid DNS label -- lower-cased, with
    ///     every character outside <c>[a-z0-9-]</c> replaced with a hyphen, leading/trailing
    ///     hyphens trimmed, and truncated to the 63-octet limit RFC 1035 Section 3.1 places on a
    ///     single label. Returns <see langword="null" /> when nothing usable survives (an absent,
    ///     blank, or entirely-punctuation host name), the signal this feature uses to skip
    ///     registration entirely rather than register a nonsensical or empty name.
    /// </summary>
    private static string SanitizeHostNameLabel(string hostName)
    {
        if (string.IsNullOrWhiteSpace(hostName))
            return null;

        char[] characters = hostName
            .Trim()
            .ToLowerInvariant()
            .Select(character =>
                char.IsAsciiLetterOrDigit(character) || character == '-' ? character : '-'
            )
            .ToArray();

        string label = new string(characters).Trim('-');

        if (label.Length == 0)
            return null;

        if (label.Length > 63)
            label = label[..63].Trim('-');

        return label.Length == 0 ? null : label;
    }
}
