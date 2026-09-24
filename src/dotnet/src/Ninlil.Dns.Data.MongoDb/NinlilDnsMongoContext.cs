using MongoDB.Driver;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb;

/// <summary>
///     This class is MongoDB's equivalent of <c>NinlilDnsContext</c> from the Entity Framework
///     Core project: a single place that knows about every collection Ninlil stores data in,
///     covering both DNS and DHCP alike (mirroring that project's own single shared model), named
///     according to a <c>NinlilDns</c>/<c>NinlilDhcp</c>-prefixed convention that matches the
///     relational table names.
/// </summary>
/// <param name="client">The MongoDB client connected to the server.</param>
/// <param name="databaseName">The name of the database within that server to use.</param>
public class NinlilDnsMongoContext(IMongoClient client, string databaseName)
{
    /// <summary>
    ///     This property contains the MongoDB database this context reads from and writes to.
    /// </summary>
    public IMongoDatabase Database { get; } = client.GetDatabase(databaseName);

    /// <summary>
    ///     This property contains every registered user.
    /// </summary>
    public IMongoCollection<UserEntity> Users =>
        Database.GetCollection<UserEntity>("NinlilDnsUsers");

    /// <summary>
    ///     This property contains every defined role.
    /// </summary>
    public IMongoCollection<RoleEntity> Roles =>
        Database.GetCollection<RoleEntity>("NinlilDnsRoles");

    /// <summary>
    ///     This property contains every user-to-role membership.
    /// </summary>
    public IMongoCollection<UserRoleEntity> UserRoles =>
        Database.GetCollection<UserRoleEntity>("NinlilDnsUserRoles");

    /// <summary>
    ///     This property contains every claim attached directly to a user.
    /// </summary>
    public IMongoCollection<UserClaimEntity> UserClaims =>
        Database.GetCollection<UserClaimEntity>("NinlilDnsUserClaims");

    /// <summary>
    ///     This property contains every claim granted by a role.
    /// </summary>
    public IMongoCollection<RoleClaimEntity> RoleClaims =>
        Database.GetCollection<RoleClaimEntity>("NinlilDnsRoleClaims");

    /// <summary>
    ///     This property contains every issued refresh-token session.
    /// </summary>
    public IMongoCollection<SessionEntity> Sessions =>
        Database.GetCollection<SessionEntity>("NinlilDnsSessions");

    /// <summary>
    ///     This property contains every issued API token.
    /// </summary>
    public IMongoCollection<ApiTokenEntity> ApiTokens =>
        Database.GetCollection<ApiTokenEntity>("NinlilDnsApiTokens");

    /// <summary>
    ///     This property contains every captured log entry.
    /// </summary>
    public IMongoCollection<LogEntity> Logs => Database.GetCollection<LogEntity>("NinlilDnsLogs");

    /// <summary>
    ///     This property contains every captured metric sample.
    /// </summary>
    public IMongoCollection<MetricEntity> Metrics =>
        Database.GetCollection<MetricEntity>("NinlilDnsMetrics");

    /// <summary>
    ///     This property contains every DNS zone.
    /// </summary>
    public IMongoCollection<ZoneEntity> Zones =>
        Database.GetCollection<ZoneEntity>("NinlilDnsZones");

    /// <summary>
    ///     This property contains every DNS resource record.
    /// </summary>
    public IMongoCollection<RecordEntity> Records =>
        Database.GetCollection<RecordEntity>("NinlilDnsRecords");

    /// <summary>
    ///     This property contains every DHCP scope (address pool).
    /// </summary>
    public IMongoCollection<DhcpScopeEntity> DhcpScopes =>
        Database.GetCollection<DhcpScopeEntity>("NinlilDhcpScopes");

    /// <summary>
    ///     This property contains every DHCP static reservation.
    /// </summary>
    public IMongoCollection<DhcpReservationEntity> DhcpReservations =>
        Database.GetCollection<DhcpReservationEntity>("NinlilDhcpReservations");

    /// <summary>
    ///     This property contains every DHCP lease.
    /// </summary>
    public IMongoCollection<LeaseEntity> Leases =>
        Database.GetCollection<LeaseEntity>("NinlilDhcpLeases");

    /// <summary>
    ///     This property contains every operator-configured DHCP scope custom option.
    /// </summary>
    public IMongoCollection<DhcpScopeCustomOptionEntity> DhcpScopeCustomOptions =>
        Database.GetCollection<DhcpScopeCustomOptionEntity>("NinlilDhcpScopeCustomOptions");

    /// <summary>
    ///     This property contains every DHCP tag-based conditional option delivery rule.
    /// </summary>
    public IMongoCollection<DhcpTagRuleEntity> DhcpTagRules =>
        Database.GetCollection<DhcpTagRuleEntity>("NinlilDhcpTagRules");

    /// <summary>
    ///     This property contains every option a DHCP tag rule contributes when it matches.
    /// </summary>
    public IMongoCollection<DhcpTagRuleOptionEntity> DhcpTagRuleOptions =>
        Database.GetCollection<DhcpTagRuleOptionEntity>("NinlilDhcpTagRuleOptions");

    /// <summary>
    ///     This property contains every pre-shared TSIG key.
    /// </summary>
    public IMongoCollection<TsigKeyEntity> TsigKeys =>
        Database.GetCollection<TsigKeyEntity>("NinlilDnsTsigKeys");

    /// <summary>
    ///     This property contains every zone's change history.
    /// </summary>
    public IMongoCollection<ZoneChangeJournalEntryEntity> ZoneChangeJournalEntries =>
        Database.GetCollection<ZoneChangeJournalEntryEntity>("NinlilDnsZoneChangeJournalEntries");

    /// <summary>
    ///     This property contains every zone's DNSSEC signing keys.
    /// </summary>
    public IMongoCollection<DnssecKeyEntity> DnssecKeys =>
        Database.GetCollection<DnssecKeyEntity>("NinlilDnsDnssecKeys");

    /// <summary>
    ///     This property contains every cloud DNS provider credential set.
    /// </summary>
    public IMongoCollection<CloudDnsProviderCredentialEntity> CloudDnsProviderCredentials =>
        Database.GetCollection<CloudDnsProviderCredentialEntity>(
            "NinlilDnsCloudDnsProviderCredentials"
        );

    /// <summary>
    ///     This property contains every block/allow list.
    /// </summary>
    public IMongoCollection<DnsFilterListEntity> DnsFilterLists =>
        Database.GetCollection<DnsFilterListEntity>("NinlilDnsFilterLists");

    /// <summary>
    ///     This property contains every block/allow list's own entries.
    /// </summary>
    public IMongoCollection<DnsFilterListEntryEntity> DnsFilterListEntries =>
        Database.GetCollection<DnsFilterListEntryEntity>("NinlilDnsFilterListEntries");

    /// <summary>
    ///     This property contains every client group.
    /// </summary>
    public IMongoCollection<DnsClientGroupEntity> DnsClientGroups =>
        Database.GetCollection<DnsClientGroupEntity>("NinlilDnsClientGroups");

    /// <summary>
    ///     This property contains every known client device this server is tracking.
    /// </summary>
    public IMongoCollection<DnsClientEntity> DnsClients =>
        Database.GetCollection<DnsClientEntity>("NinlilDnsClients");

    /// <summary>
    ///     This property contains every persistent identifier a known client device answers to --
    ///     see <see cref="DnsClientEntity" />'s own doc comment for why a device's own
    ///     identifier(s) live in this separate, one-to-many collection rather than directly on it.
    /// </summary>
    public IMongoCollection<DnsClientAddressEntity> DnsClientAddresses =>
        Database.GetCollection<DnsClientAddressEntity>("NinlilDnsClientAddresses");

    /// <summary>
    ///     This property contains every client group's own matched networks.
    /// </summary>
    public IMongoCollection<DnsClientGroupNetworkEntity> DnsClientGroupNetworks =>
        Database.GetCollection<DnsClientGroupNetworkEntity>("NinlilDnsClientGroupNetworks");

    /// <summary>
    ///     This property contains every client group's own assigned lists.
    /// </summary>
    public IMongoCollection<DnsClientGroupListAssignmentEntity> DnsClientGroupListAssignments =>
        Database.GetCollection<DnsClientGroupListAssignmentEntity>(
            "NinlilDnsClientGroupListAssignments"
        );

    /// <summary>
    ///     This property contains every individual known client's own directly assigned lists --
    ///     unioned with whatever its own matched client group already contributes, see
    ///     <c>Ninlil.Dns.Server.Filtering.DnsFilterEngine.ResolveApplicableListsAsync</c>.
    /// </summary>
    public IMongoCollection<DnsClientListAssignmentEntity> DnsClientListAssignments =>
        Database.GetCollection<DnsClientListAssignmentEntity>("NinlilDnsClientListAssignments");

    /// <summary>
    ///     This property contains every individual known client's own direct time-of-day
    ///     parental-control schedule.
    /// </summary>
    public IMongoCollection<DnsClientScheduleEntity> DnsClientSchedules =>
        Database.GetCollection<DnsClientScheduleEntity>("NinlilDnsClientSchedules");

    /// <summary>
    ///     This property contains every client group's own time-of-day parental-control
    ///     schedule.
    /// </summary>
    public IMongoCollection<DnsClientGroupScheduleEntity> DnsClientGroupSchedules =>
        Database.GetCollection<DnsClientGroupScheduleEntity>("NinlilDnsClientGroupSchedules");

    /// <summary>
    ///     This property contains every globally configured upstream forwarder.
    /// </summary>
    public IMongoCollection<DnsForwarderEntity> DnsForwarders =>
        Database.GetCollection<DnsForwarderEntity>("NinlilDnsForwarders");

    /// <summary>
    ///     This property contains every outbound webhook subscription.
    /// </summary>
    public IMongoCollection<WebhookSubscriptionEntity> WebhookSubscriptions =>
        Database.GetCollection<WebhookSubscriptionEntity>("NinlilDnsWebhookSubscriptions");

    /// <summary>
    ///     This property contains every persisted outbound webhook delivery attempt.
    /// </summary>
    public IMongoCollection<WebhookDeliveryAttemptEntity> WebhookDeliveryAttempts =>
        Database.GetCollection<WebhookDeliveryAttemptEntity>("NinlilDnsWebhookDeliveryAttempts");

    /// <summary>
    ///     This property contains every DNS rewrite.
    /// </summary>
    public IMongoCollection<DnsRewriteEntity> DnsRewrites =>
        Database.GetCollection<DnsRewriteEntity>("NinlilDnsRewrites");

    /// <summary>
    ///     This property contains every persisted, already-answered query log entry.
    /// </summary>
    public IMongoCollection<DnsQueryLogEntryEntity> DnsQueryLogEntries =>
        Database.GetCollection<DnsQueryLogEntryEntity>("NinlilDnsQueryLogEntries");

    /// <summary>
    ///     This property contains every per-client/network access-control rule.
    /// </summary>
    public IMongoCollection<DnsAccessControlRuleEntity> DnsAccessControlRules =>
        Database.GetCollection<DnsAccessControlRuleEntity>("NinlilDnsAccessControlRules");

    /// <summary>
    ///     This property contains every operator-authored, permanent pin from a source IP
    ///     address/network to a persistent client identifier -- see
    ///     <c>Ninlil.Dns.Server.ClientIdentity.StaticAddressMappingClientIdentityResolver</c>.
    /// </summary>
    public IMongoCollection<DnsClientStaticAddressMappingEntity> DnsClientStaticAddressMappings =>
        Database.GetCollection<DnsClientStaticAddressMappingEntity>(
            "NinlilDnsClientStaticAddressMappings"
        );

    /// <summary>
    ///     This property contains every peer node this instance's own cluster membership
    ///     registry knows about.
    /// </summary>
    public IMongoCollection<ClusterNodeEntity> ClusterNodes =>
        Database.GetCollection<ClusterNodeEntity>("NinlilDnsClusterNodes");

    /// <summary>
    ///     This property contains the last-write-wins bookkeeping row, one per settings section,
    ///     that the cluster's own config-sync mechanism keeps for every synced settings section.
    /// </summary>
    public IMongoCollection<ClusterSyncedSettingsStateEntity> ClusterSyncedSettingsStates =>
        Database.GetCollection<ClusterSyncedSettingsStateEntity>(
            "NinlilDnsClusterSyncedSettingsStates"
        );
}
