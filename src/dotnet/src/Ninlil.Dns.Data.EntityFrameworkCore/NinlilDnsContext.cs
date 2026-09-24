using Microsoft.EntityFrameworkCore;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore;

/// <summary>
///     This class is the single Entity Framework Core model shared by every relational provider
///     Ninlil supports, and by both this project's DNS entities and Ninlil.Dhcp's own DHCP
///     scope/reservation/lease entities -- one shared storage-engine selection and one
///     <c>IUnitOfWork</c> continue to serve the whole application rather than each protocol
///     family standing up its own separate database connection and its own separate "save"
///     boundary. DHCP's own entities are mapped here, in this project, rather than in
///     <c>Ninlil.Dhcp.Data.EntityFrameworkCore</c> itself, purely because
///     <see cref="OnModelCreating" /> only scans this assembly for
///     <see cref="Microsoft.EntityFrameworkCore.IEntityTypeConfiguration{TEntity}" />
///     implementations; the DHCP-specific repository implementations that actually read and
///     write through this context still live in their own project, exactly as every DNS
///     repository already does. This class does not know, and does not need to know, which
///     provider is actually in use underneath it -- that choice is made once, at startup, by
///     whichever <c>UseNinlilDns*</c> extension method (see
///     <see cref="Extension.DbContextOptionsBuilderExtension" />) configured the
///     <see cref="DbContextOptions" /> it was constructed with.
/// </summary>
/// <param name="options">The configured options that select and connect to a provider.</param>
public class NinlilDnsContext(DbContextOptions<NinlilDnsContext> options) : DbContext(options)
{
    /// <summary>
    ///     This property contains every registered user.
    /// </summary>
    public DbSet<UserEntity> Users => Set<UserEntity>();

    /// <summary>
    ///     This property contains every defined role.
    /// </summary>
    public DbSet<RoleEntity> Roles => Set<RoleEntity>();

    /// <summary>
    ///     This property contains every user-to-role membership.
    /// </summary>
    public DbSet<UserRoleEntity> UserRoles => Set<UserRoleEntity>();

    /// <summary>
    ///     This property contains every claim attached directly to a user.
    /// </summary>
    public DbSet<UserClaimEntity> UserClaims => Set<UserClaimEntity>();

    /// <summary>
    ///     This property contains every claim granted by a role.
    /// </summary>
    public DbSet<RoleClaimEntity> RoleClaims => Set<RoleClaimEntity>();

    /// <summary>
    ///     This property contains every issued refresh-token session.
    /// </summary>
    public DbSet<SessionEntity> Sessions => Set<SessionEntity>();

    /// <summary>
    ///     This property contains every issued API token.
    /// </summary>
    public DbSet<ApiTokenEntity> ApiTokens => Set<ApiTokenEntity>();

    /// <summary>
    ///     This property contains every captured log entry.
    /// </summary>
    public DbSet<LogEntity> Logs => Set<LogEntity>();

    /// <summary>
    ///     This property contains every captured metric sample.
    /// </summary>
    public DbSet<MetricEntity> Metrics => Set<MetricEntity>();

    /// <summary>
    ///     This property contains every DNS zone.
    /// </summary>
    public DbSet<ZoneEntity> Zones => Set<ZoneEntity>();

    /// <summary>
    ///     This property contains every DNS resource record.
    /// </summary>
    public DbSet<RecordEntity> Records => Set<RecordEntity>();

    /// <summary>
    ///     This property contains every DHCP scope (address pool).
    /// </summary>
    public DbSet<DhcpScopeEntity> DhcpScopes => Set<DhcpScopeEntity>();

    /// <summary>
    ///     This property contains every DHCP static reservation.
    /// </summary>
    public DbSet<DhcpReservationEntity> DhcpReservations => Set<DhcpReservationEntity>();

    /// <summary>
    ///     This property contains every DHCP lease.
    /// </summary>
    public DbSet<LeaseEntity> Leases => Set<LeaseEntity>();

    /// <summary>
    ///     This property contains every operator-configured DHCP scope custom option.
    /// </summary>
    public DbSet<DhcpScopeCustomOptionEntity> DhcpScopeCustomOptions =>
        Set<DhcpScopeCustomOptionEntity>();

    /// <summary>
    ///     This property contains every DHCP tag-based conditional option delivery rule.
    /// </summary>
    public DbSet<DhcpTagRuleEntity> DhcpTagRules => Set<DhcpTagRuleEntity>();

    /// <summary>
    ///     This property contains every option a DHCP tag rule contributes when it matches.
    /// </summary>
    public DbSet<DhcpTagRuleOptionEntity> DhcpTagRuleOptions => Set<DhcpTagRuleOptionEntity>();

    /// <summary>
    ///     This property contains every pre-shared TSIG key.
    /// </summary>
    public DbSet<TsigKeyEntity> TsigKeys => Set<TsigKeyEntity>();

    /// <summary>
    ///     This property contains every zone's change history.
    /// </summary>
    public DbSet<ZoneChangeJournalEntryEntity> ZoneChangeJournalEntries =>
        Set<ZoneChangeJournalEntryEntity>();

    /// <summary>
    ///     This property contains every zone's DNSSEC signing keys.
    /// </summary>
    public DbSet<DnssecKeyEntity> DnssecKeys => Set<DnssecKeyEntity>();

    /// <summary>
    ///     This property contains every cloud DNS provider credential set.
    /// </summary>
    public DbSet<CloudDnsProviderCredentialEntity> CloudDnsProviderCredentials =>
        Set<CloudDnsProviderCredentialEntity>();

    /// <summary>
    ///     This property contains every block/allow list.
    /// </summary>
    public DbSet<DnsFilterListEntity> DnsFilterLists => Set<DnsFilterListEntity>();

    /// <summary>
    ///     This property contains every block/allow list's own entries.
    /// </summary>
    public DbSet<DnsFilterListEntryEntity> DnsFilterListEntries => Set<DnsFilterListEntryEntity>();

    /// <summary>
    ///     This property contains every client group.
    /// </summary>
    public DbSet<DnsClientGroupEntity> DnsClientGroups => Set<DnsClientGroupEntity>();

    /// <summary>
    ///     This property contains every known client device this server is tracking.
    /// </summary>
    public DbSet<DnsClientEntity> DnsClients => Set<DnsClientEntity>();

    /// <summary>
    ///     This property contains every persistent identifier a known client device answers to --
    ///     see <see cref="DnsClientEntity" />'s own doc comment for why a device's own
    ///     identifier(s) live in this separate, one-to-many table rather than directly on it.
    /// </summary>
    public DbSet<DnsClientAddressEntity> DnsClientAddresses => Set<DnsClientAddressEntity>();

    /// <summary>
    ///     This property contains every client group's own matched networks.
    /// </summary>
    public DbSet<DnsClientGroupNetworkEntity> DnsClientGroupNetworks =>
        Set<DnsClientGroupNetworkEntity>();

    /// <summary>
    ///     This property contains every client group's own assigned lists.
    /// </summary>
    public DbSet<DnsClientGroupListAssignmentEntity> DnsClientGroupListAssignments =>
        Set<DnsClientGroupListAssignmentEntity>();

    /// <summary>
    ///     This property contains every individual known client's own directly assigned lists --
    ///     unioned with whatever its own matched client group already contributes, see
    ///     <c>Ninlil.Dns.Server.Filtering.DnsFilterEngine.ResolveApplicableListsAsync</c>.
    /// </summary>
    public DbSet<DnsClientListAssignmentEntity> DnsClientListAssignments =>
        Set<DnsClientListAssignmentEntity>();

    /// <summary>
    ///     This property contains every individual known client's own direct time-of-day
    ///     parental-control schedule -- see
    ///     <c>Ninlil.Dns.Server.Core.ClientSchedule.IDnsClientScheduleEngine</c>.
    /// </summary>
    public DbSet<DnsClientScheduleEntity> DnsClientSchedules => Set<DnsClientScheduleEntity>();

    /// <summary>
    ///     This property contains every client group's own time-of-day parental-control
    ///     schedule -- see <c>Ninlil.Dns.Server.Core.ClientSchedule.IDnsClientScheduleEngine</c>.
    /// </summary>
    public DbSet<DnsClientGroupScheduleEntity> DnsClientGroupSchedules =>
        Set<DnsClientGroupScheduleEntity>();

    /// <summary>
    ///     This property contains every globally configured upstream forwarder.
    /// </summary>
    public DbSet<DnsForwarderEntity> DnsForwarders => Set<DnsForwarderEntity>();

    /// <summary>
    ///     This property contains every outbound webhook subscription.
    /// </summary>
    public DbSet<WebhookSubscriptionEntity> WebhookSubscriptions =>
        Set<WebhookSubscriptionEntity>();

    /// <summary>
    ///     This property contains every persisted outbound webhook delivery attempt.
    /// </summary>
    public DbSet<WebhookDeliveryAttemptEntity> WebhookDeliveryAttempts =>
        Set<WebhookDeliveryAttemptEntity>();

    /// <summary>
    ///     This property contains every DNS rewrite.
    /// </summary>
    public DbSet<DnsRewriteEntity> DnsRewrites => Set<DnsRewriteEntity>();

    /// <summary>
    ///     This property contains every persisted, already-answered query log entry.
    /// </summary>
    public DbSet<DnsQueryLogEntryEntity> DnsQueryLogEntries => Set<DnsQueryLogEntryEntity>();

    /// <summary>
    ///     This property contains every per-client/network access-control rule.
    /// </summary>
    public DbSet<DnsAccessControlRuleEntity> DnsAccessControlRules =>
        Set<DnsAccessControlRuleEntity>();

    /// <summary>
    ///     This property contains every operator-authored, permanent pin from a source IP
    ///     address/network to a persistent client identifier -- see
    ///     <c>Ninlil.Dns.Server.ClientIdentity.StaticAddressMappingClientIdentityResolver</c>.
    /// </summary>
    public DbSet<DnsClientStaticAddressMappingEntity> DnsClientStaticAddressMappings =>
        Set<DnsClientStaticAddressMappingEntity>();

    /// <summary>
    ///     This property contains every peer node this instance's own cluster membership
    ///     registry knows about.
    /// </summary>
    public DbSet<ClusterNodeEntity> ClusterNodes => Set<ClusterNodeEntity>();

    /// <summary>
    ///     This property contains the last-write-wins bookkeeping row, one per settings section,
    ///     that the cluster's own config-sync mechanism keeps for every synced settings section.
    /// </summary>
    public DbSet<ClusterSyncedSettingsStateEntity> ClusterSyncedSettingsStates =>
        Set<ClusterSyncedSettingsStateEntity>();

    /// <summary>
    ///     This method applies every <see cref="Microsoft.EntityFrameworkCore.IEntityTypeConfiguration{TEntity}" />
    ///     found in this assembly, so that adding a new entity only ever requires adding its
    ///     configuration class alongside it rather than also remembering to register it here.
    /// </summary>
    /// <param name="modelBuilder">The model builder being configured.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Apply every entity configuration declared in this assembly.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NinlilDnsContext).Assembly);

        // Let the base class finish building the model.
        base.OnModelCreating(modelBuilder);
    }
}
