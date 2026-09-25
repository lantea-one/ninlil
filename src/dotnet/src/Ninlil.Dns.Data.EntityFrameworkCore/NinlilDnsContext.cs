using Microsoft.EntityFrameworkCore;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore;

/// <summary>
///     This class is the one Entity Framework Core model shared by every relational provider, for
///     both the DNS entities and the DHCP scope, reservation and lease entities. One storage
///     choice and one <c>IUnitOfWork</c> serve the whole application instead of a database
///     connection and save boundary per protocol. The DHCP entities are mapped in this assembly,
///     not in <c>Ninlil.Dhcp.Data.EntityFrameworkCore</c>, because <see cref="OnModelCreating" />
///     only scans this assembly for
///     <see cref="Microsoft.EntityFrameworkCore.IEntityTypeConfiguration{TEntity}" /> classes; the
///     DHCP repositories still live in their own project. The provider is chosen at startup by a
///     <c>UseNinlilDns*</c> method of <see cref="Extension.DbContextOptionsBuilderExtension" />.
///     <para>
///         The configuration classes in <c>Configuration/</c> share these conventions:
///     </para>
///     <list type="bullet">
///         <item>
///             <description>
///                 Enums are stored by name (<c>HasConversion&lt;string&gt;()</c>), so the table
///                 contents read without the code. <see cref="System.FlagsAttribute" /> enums stay
///                 integers.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <see cref="List{T}" /> of <see cref="string" /> properties are stored as JSON
///                 with <see cref="Conversion.JsonStringListValueConverter" /> and
///                 <see cref="Conversion.JsonStringListValueComparer" />, since the providers have
///                 no common array column type.
///             </description>
///         </item>
///         <item>
///             <description>
///                 A <see cref="DateTimeOffset" /> column that a query orders by or compares is
///                 stored as UTC ticks (<see cref="long" />). The SQLite provider cannot translate
///                 <c>ORDER BY</c> or comparisons over <see cref="DateTimeOffset" />, since its text
///                 form does not compare correctly across offsets. The values are always UTC, so
///                 nothing is lost and ordering works on every provider.
///             </description>
///         </item>
///         <item>
///             <description>
///                 Unsigned integers are stored as a wider signed type, since not every provider
///                 has unsigned columns.
///             </description>
///         </item>
///     </list>
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
    ///     This property contains the entries of every block/allow list.
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
    ///     This property contains every persistent identifier a known client device answers to; see
    ///     <see cref="DnsClientAddressEntity" /> for why these are a separate table.
    /// </summary>
    public DbSet<DnsClientAddressEntity> DnsClientAddresses => Set<DnsClientAddressEntity>();

    /// <summary>
    ///     This property contains the match rules of every client group.
    /// </summary>
    public DbSet<DnsClientGroupNetworkEntity> DnsClientGroupNetworks =>
        Set<DnsClientGroupNetworkEntity>();

    /// <summary>
    ///     This property contains the list assignments of every client group.
    /// </summary>
    public DbSet<DnsClientGroupListAssignmentEntity> DnsClientGroupListAssignments =>
        Set<DnsClientGroupListAssignmentEntity>();

    /// <summary>
    ///     This property contains the lists assigned directly to known clients, which add to their
    ///     group's lists (see <c>Ninlil.Dns.Server.Filtering.DnsFilterEngine.ResolveApplicableListsAsync</c>).
    /// </summary>
    public DbSet<DnsClientListAssignmentEntity> DnsClientListAssignments =>
        Set<DnsClientListAssignmentEntity>();

    /// <summary>
    ///     This property contains the parental-control schedules assigned directly to known clients
    ///     (see <c>Ninlil.Dns.Server.Core.ClientSchedule.IDnsClientScheduleEngine</c>).
    /// </summary>
    public DbSet<DnsClientScheduleEntity> DnsClientSchedules => Set<DnsClientScheduleEntity>();

    /// <summary>
    ///     This property contains the parental-control schedules of client groups (see
    ///     <c>Ninlil.Dns.Server.Core.ClientSchedule.IDnsClientScheduleEngine</c>).
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
    ///     This property contains every persisted query log entry.
    /// </summary>
    public DbSet<DnsQueryLogEntryEntity> DnsQueryLogEntries => Set<DnsQueryLogEntryEntity>();

    /// <summary>
    ///     This property contains every per-client/network access-control rule.
    /// </summary>
    public DbSet<DnsAccessControlRuleEntity> DnsAccessControlRules =>
        Set<DnsAccessControlRuleEntity>();

    /// <summary>
    ///     This property contains every static mapping from a source address or network to a
    ///     client identifier (see
    ///     <c>Ninlil.Dns.Server.ClientIdentity.StaticAddressMappingClientIdentityResolver</c>).
    /// </summary>
    public DbSet<DnsClientStaticAddressMappingEntity> DnsClientStaticAddressMappings =>
        Set<DnsClientStaticAddressMappingEntity>();

    /// <summary>
    ///     This property contains every peer in the local cluster registry.
    /// </summary>
    public DbSet<ClusterNodeEntity> ClusterNodes => Set<ClusterNodeEntity>();

    /// <summary>
    ///     This property contains the last-write-wins state cluster config sync keeps, one row per
    ///     synced settings section.
    /// </summary>
    public DbSet<ClusterSyncedSettingsStateEntity> ClusterSyncedSettingsStates =>
        Set<ClusterSyncedSettingsStateEntity>();

    /// <summary>
    ///     This method applies every <see cref="Microsoft.EntityFrameworkCore.IEntityTypeConfiguration{TEntity}" />
    ///     found in this assembly, so a new entity only needs its configuration class, not a
    ///     registration here.
    /// </summary>
    /// <param name="modelBuilder">The model builder being configured.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NinlilDnsContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
