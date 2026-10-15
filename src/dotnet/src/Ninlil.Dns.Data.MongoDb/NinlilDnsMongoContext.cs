using MongoDB.Bson;
using MongoDB.Driver;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Routing;
using Ninlil.Dns.Data.MongoDb.Routing;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb;

/// <summary>
///     This class is the MongoDB counterpart of <c>NinlilDnsContext</c>: the one place that knows
///     every collection Ninlil stores data in, for both DNS and DHCP. Collection names use the
///     same <c>NinlilDns</c>/<c>NinlilDhcp</c> prefixes as the relational table names. Class maps
///     are set up by <see cref="Configuration.MongoClassMapRegistrar" />.
///     <para>
///         There are two kinds of context over the same client. The primary context is the
///         default. The replica view reads with the read preference configured in
///         <c>Storage:MongoDb:ReadPreference</c> and refuses every write: its collections are
///         wrapped so that an insert, update, delete or find-and-modify throws
///         <see cref="StorageReadOnlyException" />. (The driver already sends a write to the
///         primary whatever the read preference says, so the wrapper is a guard against a
///         mistake, not what keeps the write off a secondary.) The scoped registration in
///         <see cref="Extension.ServiceCollectionMongoDbExtension" /> hands a scope one or the
///         other according to its <see cref="StorageReadIntent" />.
///     </para>
/// </summary>
/// <param name="client">The MongoDB client connected to the server.</param>
/// <param name="databaseName">The name of the database within that server to use.</param>
/// <param name="readPreference">
///     The read preference of this context's reads, or null to keep the client's.
/// </param>
/// <param name="readOnly">True for the replica view that refuses writes.</param>
public class NinlilDnsMongoContext(
    IMongoClient client,
    string databaseName,
    ReadPreference readPreference = null,
    bool readOnly = false
)
{
    /// <summary>
    ///     This property contains the MongoDB database this context reads from and writes to.
    /// </summary>
    public IMongoDatabase Database { get; } =
        readPreference is null
            ? client.GetDatabase(databaseName)
            : client.GetDatabase(databaseName).WithReadPreference(readPreference);

    /// <summary>
    ///     This property reports whether the context is the read-only replica view.
    /// </summary>
    public bool IsReplicaView { get; } = readOnly;

    /// <summary>
    ///     This method throws when the context is the replica view, so work that writes or must
    ///     run on the primary (the startup guard, the index setup, a unit of work) fails before it
    ///     touches the wrong server.
    /// </summary>
    /// <param name="attempt">A short description of the work, for the message.</param>
    /// <exception cref="StorageReadOnlyException">The context is the replica view.</exception>
    public void EnsureWritable(string attempt)
    {
        if (IsReplicaView)
            throw StorageReadOnlyException.For(attempt);
    }

    /// <summary>
    ///     This method returns a collection of the database, wrapped to refuse writes when the
    ///     context is the replica view.
    /// </summary>
    private IMongoCollection<TDocument> Collection<TDocument>(string name)
    {
        IMongoCollection<TDocument> collection = Database.GetCollection<TDocument>(name);
        return IsReplicaView ? ReadOnlyMongoCollection.Wrap(collection) : collection;
    }

    /// <summary>
    ///     This property contains every registered user.
    /// </summary>
    public IMongoCollection<UserEntity> Users => Collection<UserEntity>("NinlilDnsUsers");

    /// <summary>
    ///     This property contains every defined role.
    /// </summary>
    public IMongoCollection<RoleEntity> Roles => Collection<RoleEntity>("NinlilDnsRoles");

    /// <summary>
    ///     This property contains every user-to-role membership.
    /// </summary>
    public IMongoCollection<UserRoleEntity> UserRoles =>
        Collection<UserRoleEntity>("NinlilDnsUserRoles");

    /// <summary>
    ///     This property contains every claim attached directly to a user.
    /// </summary>
    public IMongoCollection<UserClaimEntity> UserClaims =>
        Collection<UserClaimEntity>("NinlilDnsUserClaims");

    /// <summary>
    ///     This property contains every claim granted by a role.
    /// </summary>
    public IMongoCollection<RoleClaimEntity> RoleClaims =>
        Collection<RoleClaimEntity>("NinlilDnsRoleClaims");

    /// <summary>
    ///     This property contains every issued refresh-token session.
    /// </summary>
    public IMongoCollection<SessionEntity> Sessions =>
        Collection<SessionEntity>("NinlilDnsSessions");

    /// <summary>
    ///     This property contains every issued API token.
    /// </summary>
    public IMongoCollection<ApiTokenEntity> ApiTokens =>
        Collection<ApiTokenEntity>("NinlilDnsApiTokens");

    /// <summary>
    ///     This property contains every captured log entry.
    /// </summary>
    public IMongoCollection<LogEntity> Logs => Collection<LogEntity>("NinlilDnsLogs");

    /// <summary>
    ///     This property contains every captured metric sample.
    /// </summary>
    public IMongoCollection<MetricEntity> Metrics => Collection<MetricEntity>("NinlilDnsMetrics");

    /// <summary>
    ///     This property contains every DNS zone.
    /// </summary>
    public IMongoCollection<ZoneEntity> Zones => Collection<ZoneEntity>("NinlilDnsZones");

    /// <summary>
    ///     This property contains every DNS resource record.
    /// </summary>
    public IMongoCollection<RecordEntity> Records => Collection<RecordEntity>("NinlilDnsRecords");

    /// <summary>
    ///     This property contains every DHCP scope (address pool).
    /// </summary>
    public IMongoCollection<DhcpScopeEntity> DhcpScopes =>
        Collection<DhcpScopeEntity>("NinlilDhcpScopes");

    /// <summary>
    ///     This property contains every DHCP static reservation.
    /// </summary>
    public IMongoCollection<DhcpReservationEntity> DhcpReservations =>
        Collection<DhcpReservationEntity>("NinlilDhcpReservations");

    /// <summary>
    ///     This property contains every DHCP lease.
    /// </summary>
    public IMongoCollection<LeaseEntity> Leases => Collection<LeaseEntity>("NinlilDhcpLeases");

    /// <summary>
    ///     This property contains every operator-configured DHCP scope custom option.
    /// </summary>
    public IMongoCollection<DhcpScopeCustomOptionEntity> DhcpScopeCustomOptions =>
        Collection<DhcpScopeCustomOptionEntity>("NinlilDhcpScopeCustomOptions");

    /// <summary>
    ///     This property contains every DHCP tag-based conditional option delivery rule.
    /// </summary>
    public IMongoCollection<DhcpTagRuleEntity> DhcpTagRules =>
        Collection<DhcpTagRuleEntity>("NinlilDhcpTagRules");

    /// <summary>
    ///     This property contains every option a DHCP tag rule contributes when it matches.
    /// </summary>
    public IMongoCollection<DhcpTagRuleOptionEntity> DhcpTagRuleOptions =>
        Collection<DhcpTagRuleOptionEntity>("NinlilDhcpTagRuleOptions");

    /// <summary>
    ///     This property contains every pre-shared TSIG key.
    /// </summary>
    public IMongoCollection<TsigKeyEntity> TsigKeys =>
        Collection<TsigKeyEntity>("NinlilDnsTsigKeys");

    /// <summary>
    ///     This property contains every zone's change history.
    /// </summary>
    public IMongoCollection<ZoneChangeJournalEntryEntity> ZoneChangeJournalEntries =>
        Collection<ZoneChangeJournalEntryEntity>("NinlilDnsZoneChangeJournalEntries");

    /// <summary>
    ///     This property contains when each secondary zone was last refreshed (node-local state).
    /// </summary>
    public IMongoCollection<SecondaryZoneRefreshStateEntity> SecondaryZoneRefreshStates =>
        Collection<SecondaryZoneRefreshStateEntity>("NinlilDnsSecondaryZoneRefreshStates");

    /// <summary>
    ///     This property contains every zone's DNSSEC signing keys.
    /// </summary>
    public IMongoCollection<DnssecKeyEntity> DnssecKeys =>
        Collection<DnssecKeyEntity>("NinlilDnsDnssecKeys");

    /// <summary>
    ///     This property contains every cloud DNS provider credential set.
    /// </summary>
    public IMongoCollection<CloudDnsProviderCredentialEntity> CloudDnsProviderCredentials =>
        Collection<CloudDnsProviderCredentialEntity>("NinlilDnsCloudDnsProviderCredentials");

    /// <summary>
    ///     This property contains every block/allow list.
    /// </summary>
    public IMongoCollection<DnsFilterListEntity> DnsFilterLists =>
        Collection<DnsFilterListEntity>("NinlilDnsFilterLists");

    /// <summary>
    ///     This property contains the entries of every block/allow list.
    /// </summary>
    public IMongoCollection<DnsFilterListEntryEntity> DnsFilterListEntries =>
        Collection<DnsFilterListEntryEntity>("NinlilDnsFilterListEntries");

    /// <summary>
    ///     This property contains every client group.
    /// </summary>
    public IMongoCollection<DnsClientGroupEntity> DnsClientGroups =>
        Collection<DnsClientGroupEntity>("NinlilDnsClientGroups");

    /// <summary>
    ///     This property contains every known client device this server is tracking.
    /// </summary>
    public IMongoCollection<DnsClientEntity> DnsClients =>
        Collection<DnsClientEntity>("NinlilDnsClients");

    /// <summary>
    ///     This property contains every persistent identifier a known client device answers to; see
    ///     <see cref="DnsClientAddressEntity" /> for why these are a separate collection.
    /// </summary>
    public IMongoCollection<DnsClientAddressEntity> DnsClientAddresses =>
        Collection<DnsClientAddressEntity>("NinlilDnsClientAddresses");

    /// <summary>
    ///     This property contains the match rules of every client group.
    /// </summary>
    public IMongoCollection<DnsClientGroupNetworkEntity> DnsClientGroupNetworks =>
        Collection<DnsClientGroupNetworkEntity>("NinlilDnsClientGroupNetworks");

    /// <summary>
    ///     This property contains the list assignments of every client group.
    /// </summary>
    public IMongoCollection<DnsClientGroupListAssignmentEntity> DnsClientGroupListAssignments =>
        Collection<DnsClientGroupListAssignmentEntity>("NinlilDnsClientGroupListAssignments");

    /// <summary>
    ///     This property contains the lists assigned directly to known clients, which add to their
    ///     group's lists (see <c>Ninlil.Dns.Server.Filtering.DnsFilterEngine.ResolveApplicableListsAsync</c>).
    /// </summary>
    public IMongoCollection<DnsClientListAssignmentEntity> DnsClientListAssignments =>
        Collection<DnsClientListAssignmentEntity>("NinlilDnsClientListAssignments");

    /// <summary>
    ///     This property contains the parental-control schedules assigned directly to known clients.
    /// </summary>
    public IMongoCollection<DnsClientScheduleEntity> DnsClientSchedules =>
        Collection<DnsClientScheduleEntity>("NinlilDnsClientSchedules");

    /// <summary>
    ///     This property contains the parental-control schedules of client groups.
    /// </summary>
    public IMongoCollection<DnsClientGroupScheduleEntity> DnsClientGroupSchedules =>
        Collection<DnsClientGroupScheduleEntity>("NinlilDnsClientGroupSchedules");

    /// <summary>
    ///     This property contains every globally configured upstream forwarder.
    /// </summary>
    public IMongoCollection<DnsForwarderEntity> DnsForwarders =>
        Collection<DnsForwarderEntity>("NinlilDnsForwarders");

    /// <summary>
    ///     This property contains every outbound webhook subscription.
    /// </summary>
    public IMongoCollection<WebhookSubscriptionEntity> WebhookSubscriptions =>
        Collection<WebhookSubscriptionEntity>("NinlilDnsWebhookSubscriptions");

    /// <summary>
    ///     This property contains every persisted outbound webhook delivery attempt.
    /// </summary>
    public IMongoCollection<WebhookDeliveryAttemptEntity> WebhookDeliveryAttempts =>
        Collection<WebhookDeliveryAttemptEntity>("NinlilDnsWebhookDeliveryAttempts");

    /// <summary>
    ///     This property contains every DNS rewrite.
    /// </summary>
    public IMongoCollection<DnsRewriteEntity> DnsRewrites =>
        Collection<DnsRewriteEntity>("NinlilDnsRewrites");

    /// <summary>
    ///     This property contains every persisted query log entry.
    /// </summary>
    public IMongoCollection<DnsQueryLogEntryEntity> DnsQueryLogEntries =>
        Collection<DnsQueryLogEntryEntity>("NinlilDnsQueryLogEntries");

    /// <summary>
    ///     This property contains every per-client/network access-control rule.
    /// </summary>
    public IMongoCollection<DnsAccessControlRuleEntity> DnsAccessControlRules =>
        Collection<DnsAccessControlRuleEntity>("NinlilDnsAccessControlRules");

    /// <summary>
    ///     This property contains every static mapping from a source address or network to a
    ///     client identifier (see
    ///     <c>Ninlil.Dns.Server.ClientIdentity.StaticAddressMappingClientIdentityResolver</c>).
    /// </summary>
    public IMongoCollection<DnsClientStaticAddressMappingEntity> DnsClientStaticAddressMappings =>
        Collection<DnsClientStaticAddressMappingEntity>("NinlilDnsClientStaticAddressMappings");

    /// <summary>
    ///     This property contains every peer in the local cluster registry.
    /// </summary>
    public IMongoCollection<ClusterNodeEntity> ClusterNodes =>
        Collection<ClusterNodeEntity>("NinlilDnsClusterNodes");

    /// <summary>
    ///     This property contains the last-write-wins state cluster config sync keeps, one document
    ///     per synced settings section.
    /// </summary>
    public IMongoCollection<ClusterSyncedSettingsStateEntity> ClusterSyncedSettingsStates =>
        Collection<ClusterSyncedSettingsStateEntity>("NinlilDnsClusterSyncedSettingsStates");

    /// <summary>
    ///     This property contains the deletions cluster config sync remembers, one document per
    ///     deleted synced entity.
    /// </summary>
    public IMongoCollection<ClusterSyncTombstoneEntity> ClusterSyncTombstones =>
        Collection<ClusterSyncTombstoneEntity>("NinlilDnsClusterSyncTombstones");

    /// <summary>
    ///     This property contains one raw document per applied schema step (see
    ///     <see cref="Maintenance.MongoSchemaSteps" />), keyed by the step name. Startup compares
    ///     it with the steps this build knows to detect a database a newer version has upgraded.
    /// </summary>
    public IMongoCollection<BsonDocument> SchemaSteps =>
        Collection<BsonDocument>("NinlilDnsSchemaSteps");
}
