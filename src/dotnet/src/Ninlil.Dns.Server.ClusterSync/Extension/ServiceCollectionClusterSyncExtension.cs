using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Server.ClusterSync.AccessControl;
using Ninlil.Dns.Server.ClusterSync.Filtering;
using Ninlil.Dns.Server.ClusterSync.Forwarding;
using Ninlil.Dns.Server.ClusterSync.Identity;
using Ninlil.Dns.Server.ClusterSync.Rewrites;
using Ninlil.Dns.Server.ClusterSync.Settings;
using Ninlil.Dns.Server.ClusterSync.Shared;
using Ninlil.Dns.Server.Core.ClusterSync;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up cluster
///     config sync: an <see cref="IClusterConfigSyncApplier" /> and an event subscriber for each
///     synced category (filter lists, filter list entries, access-control rules, rewrites,
///     forwarders, users, roles), plus the sending subscriber for settings sections, whose
///     applier lives in <c>Ninlil.Web</c>. Each category also has an
///     <see cref="IClusterConfigSyncSource" />, shared by its live subscriber and by a backfill,
///     and tombstones have one of their own. The composition root must also register an
///     <see cref="IClusterConfigSyncBroadcaster" /> (<c>ClusterConfigSyncBroadcaster</c> in
///     <c>Ninlil.Dns.Server.Cluster</c>) and the repositories.
/// </summary>
public static class ServiceCollectionClusterSyncExtension
{
    /// <summary>
    ///     This method registers every applier, subscriber and source in this project, all scoped:
    ///     the appliers and sources use scoped repositories, and <c>DnsServerEventDispatcher</c> resolves
    ///     subscribers in a new scope per event.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDnsServerClusterSync(this IServiceCollection instance)
    {
        instance.AddScoped<IClusterConfigSyncApplier, FilterListClusterConfigSyncApplier>();
        instance.AddScoped<IClusterConfigSyncApplier, FilterListEntryClusterConfigSyncApplier>();
        instance.AddScoped<IClusterConfigSyncApplier, AccessControlRuleClusterConfigSyncApplier>();
        instance.AddScoped<IClusterConfigSyncApplier, RewriteClusterConfigSyncApplier>();
        instance.AddScoped<IClusterConfigSyncApplier, ForwarderClusterConfigSyncApplier>();
        instance.AddScoped<IClusterConfigSyncApplier, UserClusterConfigSyncApplier>();
        instance.AddScoped<IClusterConfigSyncApplier, RoleClusterConfigSyncApplier>();

        instance.AddScoped<
            IDnsServerEventSubscriber<DnsFilterListChangedEvent>,
            FilterListClusterSyncEventSubscriber
        >();
        instance.AddScoped<
            IDnsServerEventSubscriber<DnsFilterListEntryChangedEvent>,
            FilterListEntryClusterSyncEventSubscriber
        >();
        instance.AddScoped<
            IDnsServerEventSubscriber<DnsAccessControlRuleChangedEvent>,
            AccessControlRuleClusterSyncEventSubscriber
        >();
        instance.AddScoped<
            IDnsServerEventSubscriber<DnsRewriteChangedEvent>,
            RewriteClusterSyncEventSubscriber
        >();
        instance.AddScoped<
            IDnsServerEventSubscriber<DnsForwarderChangedEvent>,
            ForwarderClusterSyncEventSubscriber
        >();
        instance.AddScoped<
            IDnsServerEventSubscriber<ClusterConfigSettingsChangedEvent>,
            SettingsClusterSyncEventSubscriber
        >();
        instance.AddScoped<
            IDnsServerEventSubscriber<UserAccountChangedEvent>,
            UserClusterSyncEventSubscriber
        >();
        instance.AddScoped<
            IDnsServerEventSubscriber<RoleChangedEvent>,
            RoleClusterSyncEventSubscriber
        >();

        instance.AddClusterConfigSyncSource<
            FilterListClusterConfigSyncSource,
            DnsFilterListEntity
        >();
        instance.AddClusterConfigSyncSource<
            FilterListEntryClusterConfigSyncSource,
            DnsFilterListEntryEntity
        >();
        instance.AddClusterConfigSyncSource<
            AccessControlRuleClusterConfigSyncSource,
            DnsAccessControlRuleEntity
        >();
        instance.AddClusterConfigSyncSource<RewriteClusterConfigSyncSource, DnsRewriteEntity>();
        instance.AddClusterConfigSyncSource<ForwarderClusterConfigSyncSource, DnsForwarderEntity>();
        instance.AddClusterConfigSyncSource<UserClusterConfigSyncSource, UserEntity>();
        instance.AddClusterConfigSyncSource<RoleClusterConfigSyncSource, RoleEntity>();
        instance.AddScoped<IClusterConfigSyncSource, TombstoneClusterConfigSyncSource>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }

    /// <summary>
    ///     This method registers one entity source under the three types its callers resolve: its
    ///     own type, <see cref="ClusterConfigSyncSourceBase{TEntity}" /> (which the live
    ///     subscriber of the entity resolves in its scope) and
    ///     <see cref="IClusterConfigSyncSource" /> (which a backfill enumerates). All three
    ///     resolve to the same scoped instance.
    /// </summary>
    private static void AddClusterConfigSyncSource<TSource, TEntity>(
        this IServiceCollection instance
    )
        where TSource : ClusterConfigSyncSourceBase<TEntity>
        where TEntity : AuditableEntityBase
    {
        instance.AddScoped<TSource>();
        instance.AddScoped<ClusterConfigSyncSourceBase<TEntity>>(provider =>
            provider.GetRequiredService<TSource>()
        );
        instance.AddScoped<IClusterConfigSyncSource>(provider =>
            provider.GetRequiredService<TSource>()
        );
    }
}
