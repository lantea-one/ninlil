using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Server.ClusterSync.AccessControl;
using Ninlil.Dns.Server.ClusterSync.Filtering;
using Ninlil.Dns.Server.ClusterSync.Forwarding;
using Ninlil.Dns.Server.ClusterSync.Identity;
using Ninlil.Dns.Server.ClusterSync.Rewrites;
using Ninlil.Dns.Server.ClusterSync.Settings;
using Ninlil.Dns.Server.Core.ClusterSync;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up cluster
///     config sync: an <see cref="IClusterConfigSyncApplier" /> and an event subscriber for each
///     synced category (filter lists, filter list entries, access-control rules, rewrites,
///     forwarders, users, roles), plus the sending subscriber for settings sections, whose
///     applier lives in <c>Ninlil.Web</c>. The composition root must also register an
///     <see cref="IClusterConfigSyncBroadcaster" /> (<c>ClusterConfigSyncBroadcaster</c> in
///     <c>Ninlil.Dns.Server.Cluster</c>) and the repositories.
/// </summary>
public static class ServiceCollectionClusterSyncExtension
{
    /// <summary>
    ///     This method registers every applier and subscriber in this project, all scoped: the
    ///     appliers use scoped repositories, and <c>DnsServerEventDispatcher</c> resolves
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

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
