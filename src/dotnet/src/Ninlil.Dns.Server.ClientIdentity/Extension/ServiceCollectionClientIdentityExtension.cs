using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Server.ClientIdentity.Presence;
using Ninlil.Dns.Server.ClientIdentity.Presence.Netlink;
using Ninlil.Dns.Server.ClientIdentity.Vendor;
using Ninlil.Dns.Server.Core.ClientIdentity;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     Ninlil's ARP/neighbor-table and manual-static-mapping client identity resolvers.
/// </summary>
public static class ServiceCollectionClientIdentityExtension
{
    /// <summary>
    ///     This method registers <see cref="ArpNeighborTableClientIdentityResolver" /> as a
    ///     singleton, under both its own concrete type (so a composition root can compose it into
    ///     a <c>CompositeClientIdentityResolver</c> alongside other resolvers) and
    ///     <see cref="IHostedService" /> (so the host starts its background refresh loop) -- the
    ///     exact same "one singleton, two registrations" shape
    ///     <c>Ninlil.Dns.Server.AccessControl.Extension.ServiceCollectionAccessControlExtension</c>
    ///     already uses for <c>DnsAccessControlRuleCache</c>. Also registers
    ///     <see cref="StaticAddressMappingClientIdentityResolver" /> as a singleton, under its own
    ///     concrete type only -- it needs no <see cref="IHostedService" /> registration at all,
    ///     since (unlike the ARP resolver's polled snapshot) it re-queries its own repository
    ///     fresh on every single call and so has no background refresh loop to start.
    ///     <para>
    ///         This method deliberately does <em>not</em> also register
    ///         <c>Ninlil.Dns.Server.Core.ClientIdentity.IClientIdentityResolver</c> itself -- which
    ///         concrete resolver(s) actually answer that interface, and in what fallback order, is
    ///         a composition-root decision made explicitly in <c>Ninlil.Web/Program.cs</c> (see
    ///         <c>CompositeClientIdentityResolver</c>'s own doc comment), exactly like every other
    ///         "select the active implementation once" precedent in this project.
    ///     </para>
    ///     <para>
    ///         Also registers passive client presence tracking: <see cref="ClientPresenceOptions" />,
    ///         the in-memory <see cref="ClientPresenceTracker" /> (as <see cref="IClientPresenceRecorder" />
    ///         too, so any project can record a sighting through the <c>Server.Core</c> contract),
    ///         the background flush and neighbor-table sampling services, and the DNS query
    ///         subscriber that turns every answered query into a sighting.
    ///     </para>
    ///     <para>
    ///         Also registers MAC vendor lookup: <see cref="MacVendorLookupOptions" />, the
    ///         <see cref="MacVendorLookupService" /> singleton (as <see cref="IMacVendorLookupService" />
    ///         too), and <see cref="MacVendorRefreshService" /> -- a singleton reachable by its own
    ///         type for an on-demand refresh, and the hosted service running the periodic one --
    ///         with its own named <see cref="System.Net.Http.HttpClient" />.
    ///     </para>
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The configuration <see cref="ClientPresenceOptions" /> is bound from.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDnsServerClientIdentity(
        this IServiceCollection instance,
        IConfiguration configuration
    )
    {
        instance.AddSingleton<ArpNeighborTableClientIdentityResolver>();
        instance.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<ArpNeighborTableClientIdentityResolver>()
        );

        instance.AddSingleton<StaticAddressMappingClientIdentityResolver>();

        // Passive presence tracking: one singleton buffer, reachable both as itself (for the flush
        // service to drain) and through the Server.Core recording contract.
        instance.Configure<ClientPresenceOptions>(
            configuration.GetSection(ClientPresenceOptions.SectionName)
        );
        instance.AddSingleton<ClientPresenceTracker>();
        instance.AddSingleton<IClientPresenceRecorder>(provider =>
            provider.GetRequiredService<ClientPresenceTracker>()
        );
        instance.AddSingleton<INeighborTableReader, NetlinkNeighborTableReader>();
        instance.AddHostedService<ClientPresenceFlushService>();
        instance.AddHostedService<NeighborTablePresenceService>();
        instance.AddSingleton<
            IDnsServerEventSubscriber<DnsQueryAnsweredEvent>,
            DnsQueryPresenceSubscriber
        >();

        // MAC vendor lookup: one table-holding singleton, reachable through its interface for
        // lookups and by its concrete type for the refresh service's atomic swap; the refresh
        // service itself is one singleton registered twice, like the ARP resolver above.
        instance.Configure<MacVendorLookupOptions>(
            configuration.GetSection(MacVendorLookupOptions.SectionName)
        );
        instance.AddSingleton<MacVendorLookupService>();
        instance.AddSingleton<IMacVendorLookupService>(provider =>
            provider.GetRequiredService<MacVendorLookupService>()
        );
        instance.AddSingleton<MacVendorRefreshService>();
        instance.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<MacVendorRefreshService>()
        );
        instance.AddHttpClient(
            MacVendorRefreshService.HttpClientName,
            client =>
            {
                // The largest registry file is a few megabytes; allow a slow link, but never hang.
                client.Timeout = TimeSpan.FromMinutes(5);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Ninlil-MacVendorRefresh/1.0");
            }
        );

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
