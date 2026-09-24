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
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up Phase
///     9-6c's operational config sync: one <see cref="IClusterConfigSyncApplier" /> and one
///     event subscriber per synced category (filter lists, filter list entries, access-control
///     rules, rewrites, forwarders). The composition root (<c>Ninlil.Web</c>'s own
///     <c>Program.cs</c>) is expected to have already registered a real
///     <see cref="IClusterConfigSyncBroadcaster" /> (<c>Ninlil.Dns.Server.Cluster</c>'s own
///     <c>ClusterConfigSyncBroadcaster</c>) and every repository each applier/subscriber
///     depends on.
/// </summary>
public static class ServiceCollectionClusterSyncExtension
{
    /// <summary>
    ///     This method registers every applier and subscriber this project provides. Both kinds
    ///     are registered scoped -- an applier depends directly on a scoped repository/unit of
    ///     work, and a subscriber is resolved by <c>DnsServerEventDispatcher</c> through a fresh
    ///     scope per event regardless of its own declared lifetime, the same "scoped subscriber"
    ///     convention every other <c>IDnsServerEventSubscriber{TEvent}</c> registration in this
    ///     project already follows.
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
