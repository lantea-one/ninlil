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
///     This class registers DHCP clients in DNS. When a lease is granted it adds an A record (AAAA
///     for a DHCPv6 lease) for the client's host name and leased address in the zone named by the
///     scope's <see cref="DhcpScopeEntity.DnsZoneId" />; when the lease is released or expires it
///     removes the record. <see cref="DhcpLeaseDnsRegistrationEventSubscriber" /> calls it for each
///     <c>DhcpLeaseChangedEvent</c>. Writes go through <see cref="IZoneUpdateService" />, like every
///     other feature that changes zone content, and only records marked
///     <see cref="RecordEntity.ManagedByFeature" /> = <see cref="RecordManagedByFeatureEnum.Dhcp" />
///     are touched, as <see cref="Ninlil.Dns.Server.Authoritative.Ptr.PtrSyncService" /> and
///     <c>CaaRecordReconciliationService</c> do for theirs. Conflicts are checked only against
///     these managed records: a client's name replaces another DHCP client's record of the same
///     name, and is added next to a static record of the same name.
///     <para>
///         <b>PTR records.</b> Only the forward record is written here.
///         <see cref="IZoneUpdateService.ApplyChangesAsync" /> publishes a <c>DnsZoneChangedEvent</c>,
///         and <c>PtrSyncEventSubscriber</c> reconciles the PTR records for zones with PTR
///         management on, so no separate PTR write is needed.
///     </para>
///     <para>
///         <b>No caching.</b> The scope and zone are read for every lease event. Lease events are
///         rare compared with queries, and a fresh read picks up a changed
///         <see cref="DhcpScopeEntity.DnsZoneId" /> at once.
///     </para>
/// </summary>
/// <param name="leaseRepository">The repository the lease named by the event is read from.</param>
/// <param name="scopeRepository">The repository the lease's scope is read from.</param>
/// <param name="zoneRepository">The repository the scope's DNS zone is read from.</param>
/// <param name="recordRepository">The repository the zone's managed records are read from.</param>
/// <param name="zoneUpdateService">The service every addition and removal is applied through.</param>
/// <param name="logger">The logger failed zone updates are recorded to.</param>
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
    ///     This constant contains the TTL, in seconds, of every A/AAAA record this service creates.
    ///     It is shorter than the 3600 seconds <c>PtrSyncService</c> and
    ///     <c>CaaRecordReconciliationService</c> use, because a client's address changes far more
    ///     often, and resolvers should not cache a departed client's address for long.
    /// </summary>
    private const uint DhcpRecordTimeToLiveSeconds = 300;

    /// <summary>
    ///     This method handles one <c>DhcpLeaseChangedEvent</c>: it reads the lease (the event
    ///     carries only its identifier) and adds, replaces or removes its A/AAAA record. It does
    ///     nothing when the scope has no <see cref="DhcpScopeEntity.DnsZoneId" />, the zone is
    ///     missing or not a Primary or CloudPrimary zone, the address does not parse, or, for a
    ///     grant, the lease has no usable host name. A release or expiry removes the managed
    ///     record for the lease's address.
    /// </summary>
    /// <param name="leaseId">The identifier of the lease that changed.</param>
    /// <param name="changeKind">What happened to the lease.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>What was done, or why nothing was done.</returns>
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
            // Already correct: skip the write, which would publish a DnsZoneChangedEvent and
            // set off PtrSyncEventSubscriber and the other zone subscribers for nothing.
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
    ///     This method reports whether records are registered in a zone of this type: only
    ///     <see cref="ZoneTypeEnum.Primary" /> and <see cref="ZoneTypeEnum.CloudPrimary" />, whose
    ///     content this server owns. It is a named method, like
    ///     <c>PtrSyncService.IsEligibleZoneType</c> and
    ///     <c>CaaRecordReconciliationService.IsEligibleZoneType</c>, so a new zone type is not
    ///     missed in an inline check.
    /// </summary>
    /// <param name="zoneType">The zone type to check.</param>
    /// <returns><see langword="true" /> if eligible, <see langword="false" /> otherwise.</returns>
    public static bool IsEligibleZoneType(ZoneTypeEnum zoneType) =>
        zoneType == ZoneTypeEnum.Primary || zoneType == ZoneTypeEnum.CloudPrimary;

    /// <summary>
    ///     This method applies record additions and removals through
    ///     <see cref="IZoneUpdateService" />. An <see cref="InvalidOperationException" />, expected
    ///     when the zone has no SOA record to advance, is logged at debug level and ignored, as in
    ///     <c>PtrSyncService</c>. Unlike PTR sync, nothing retries the change later: this service
    ///     runs only on lease events, so the record appears at the client's next grant.
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
    ///     This method reports whether a stored A/AAAA record holds the lease's address; a record
    ///     whose data cannot be decoded never matches.
    /// </summary>
    private static bool AddressEquals(RecordEntity record, IPAddress leaseAddress) =>
        RecordEntityCodec.DecodeTypedData(record) switch
        {
            ARecordData a => a.Address.Equals(leaseAddress),
            AaaaRecordData aaaa => aaaa.Address.Equals(leaseAddress),
            _ => false,
        };

    /// <summary>
    ///     This method builds the A/AAAA record for a lease, marked as managed by DHCP.
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
    ///     This method normalizes an owner name for comparison that ignores case and a trailing
    ///     dot, like the helper of the same name in <c>PtrSyncService</c>.
    /// </summary>
    private static string NormalizeName(string name) => name.TrimEnd('.').ToLowerInvariant();

    /// <summary>
    ///     This method turns a client's free-form host name (the RFC 2132 Section 3.14 Host Name
    ///     option, or the first label of a DHCPv6 Client FQDN) into one DNS label: lower case,
    ///     characters outside <c>[a-z0-9-]</c> replaced by hyphens, outer hyphens trimmed, and cut
    ///     to the 63-octet label limit of RFC 1035 Section 3.1. It returns <see langword="null" />
    ///     when nothing usable is left (no name, blank, or only punctuation), and registration is
    ///     then skipped.
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
