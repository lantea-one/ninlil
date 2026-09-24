using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Backup.Catalog;

/// <summary>
///     This class lists every entity type a full-system backup covers, in an order that is safe
///     to import forward (a referenced row, such as a zone, always lands before the rows that
///     reference it, such as its own records) and safe to wipe in reverse (a referencing row is
///     always removed before the row it references). Relational providers configure a cascading
///     foreign key for most of these relationships already, but this class never relies on that --
///     MongoDB has no equivalent mechanism at all, so both providers are wiped and re-imported
///     through the identical explicit sequence below.
///     <para>
///         <b>What is covered</b>: every configuration and identity table, including the known-client
///         registry and everything hanging off it, access-control rules, cluster peers and their
///         settings-sync bookkeeping, and every DHCP table -- scopes, reservations, custom options,
///         tag rules and leases. Leases are live operational state rather than configuration, but
///         they are included deliberately: the storage-engine hot-switch moves data exclusively
///         through this backup/restore path, and a switch that dropped every active lease would
///         let this server hand a still-leased address to a second device. A restored lease that
///         has since lapsed is simply reclaimed by the ordinary expiry sweep.
///     </para>
///     <para>
///         <b>Deliberately excluded, and why</b>: only history/telemetry tables, none of which a
///         server needs in order to function after a restore and all of which can grow very large
///         -- <see cref="LogEntity" />/<see cref="MetricEntity" /> (operational telemetry);
///         <see cref="ZoneChangeJournalEntryEntity" /> (the incremental-transfer journal, whose
///         absence costs nothing beyond a secondary's next refresh falling back from IXFR to a
///         full AXFR once); <see cref="DnsQueryLogEntryEntity" /> (the persistent query log); and
///         <see cref="WebhookDeliveryAttemptEntity" /> (webhook delivery history). A new
///         configuration entity belongs in this catalog; only a new history table belongs in
///         this list.
///     </para>
/// </summary>
public static class BackupEntityCatalog
{
    /// <summary>
    ///     This property contains every entity type a backup covers, already ordered so that
    ///     importing the list forward, or wiping it in reverse, never violates a foreign key.
    /// </summary>
    public static IReadOnlyList<BackupEntityDescriptor> Entries { get; } =
    [
        BackupEntityDescriptor.For<RoleEntity, IRoleRepository>("Roles"),
        BackupEntityDescriptor.For<UserEntity, IUserRepository>("Users"),
        BackupEntityDescriptor.For<SessionEntity, ISessionRepository>("Sessions"),
        BackupEntityDescriptor.For<ApiTokenEntity, IApiTokenRepository>("ApiTokens"),
        BackupEntityDescriptor.For<ZoneEntity, IZoneRepository>("Zones"),
        BackupEntityDescriptor.For<RecordEntity, IRecordRepository>("Records"),
        BackupEntityDescriptor.For<TsigKeyEntity, ITsigKeyRepository>("TsigKeys"),
        BackupEntityDescriptor.For<ClusterNodeEntity, IClusterNodeRepository>("ClusterNodes"),
        BackupEntityDescriptor.For<
            ClusterSyncedSettingsStateEntity,
            IClusterSyncedSettingsStateRepository
        >("ClusterSyncedSettingsStates"),
        BackupEntityDescriptor.For<DnssecKeyEntity, IDnssecKeyRepository>("DnssecKeys"),
        BackupEntityDescriptor.For<
            CloudDnsProviderCredentialEntity,
            ICloudDnsProviderCredentialRepository
        >("CloudDnsProviderCredentials"),
        BackupEntityDescriptor.For<DnsFilterListEntity, IDnsFilterListRepository>("DnsFilterLists"),
        BackupEntityDescriptor.For<DnsFilterListEntryEntity, IDnsFilterListEntryRepository>(
            "DnsFilterListEntries"
        ),
        BackupEntityDescriptor.For<DnsClientGroupEntity, IDnsClientGroupRepository>(
            "DnsClientGroups"
        ),
        BackupEntityDescriptor.For<DnsClientGroupNetworkEntity, IDnsClientGroupNetworkRepository>(
            "DnsClientGroupNetworks"
        ),
        BackupEntityDescriptor.For<
            DnsClientGroupListAssignmentEntity,
            IDnsClientGroupListAssignmentRepository
        >("DnsClientGroupListAssignments"),
        BackupEntityDescriptor.For<DnsClientGroupScheduleEntity, IDnsClientGroupScheduleRepository>(
            "DnsClientGroupSchedules"
        ),
        BackupEntityDescriptor.For<DnsClientEntity, IDnsClientRepository>("DnsClients"),
        BackupEntityDescriptor.For<DnsClientAddressEntity, IDnsClientAddressRepository>(
            "DnsClientAddresses"
        ),
        BackupEntityDescriptor.For<
            DnsClientListAssignmentEntity,
            IDnsClientListAssignmentRepository
        >("DnsClientListAssignments"),
        BackupEntityDescriptor.For<DnsClientScheduleEntity, IDnsClientScheduleRepository>(
            "DnsClientSchedules"
        ),
        BackupEntityDescriptor.For<
            DnsClientStaticAddressMappingEntity,
            IDnsClientStaticAddressMappingRepository
        >("DnsClientStaticAddressMappings"),
        BackupEntityDescriptor.For<DnsAccessControlRuleEntity, IDnsAccessControlRuleRepository>(
            "DnsAccessControlRules"
        ),
        BackupEntityDescriptor.For<DnsForwarderEntity, IDnsForwarderRepository>("DnsForwarders"),
        BackupEntityDescriptor.For<WebhookSubscriptionEntity, IWebhookSubscriptionRepository>(
            "WebhookSubscriptions"
        ),
        BackupEntityDescriptor.For<DnsRewriteEntity, IDnsRewriteRepository>("DnsRewrites"),
        BackupEntityDescriptor.For<DhcpScopeEntity, IDhcpScopeRepository>("DhcpScopes"),
        BackupEntityDescriptor.For<DhcpReservationEntity, IDhcpReservationRepository>(
            "DhcpReservations"
        ),
        BackupEntityDescriptor.For<DhcpScopeCustomOptionEntity, IDhcpScopeCustomOptionRepository>(
            "DhcpScopeCustomOptions"
        ),
        BackupEntityDescriptor.For<DhcpTagRuleEntity, IDhcpTagRuleRepository>("DhcpTagRules"),
        BackupEntityDescriptor.For<DhcpTagRuleOptionEntity, IDhcpTagRuleOptionRepository>(
            "DhcpTagRuleOptions"
        ),
        BackupEntityDescriptor.For<LeaseEntity, ILeaseRepository>("DhcpLeases"),
    ];
}
