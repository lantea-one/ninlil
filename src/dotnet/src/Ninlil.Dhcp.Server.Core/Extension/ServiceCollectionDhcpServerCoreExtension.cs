using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Protocol.Dhcpv4;
using Ninlil.Dhcp.Protocol.Dhcpv6;
using Ninlil.Dhcp.Server.Core.Administration;
using Ninlil.Dhcp.Server.Core.Allocation;
using Ninlil.Dhcp.Server.Core.Identity;
using Ninlil.Dhcp.Server.Core.Options;
using Ninlil.Dhcp.Server.Core.Scope;
using Ninlil.Dhcp.Server.Core.Statistics;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up Ninlil's
///     real DHCPv4 and DHCPv6 server engines -- <see cref="AddNinlilDhcpServerCore" /> and
///     <see cref="AddNinlilDhcpv6ServerCore" /> respectively -- plus, via
///     <see cref="AddNinlilDhcpLeaseAdministration" />, the two protocol-agnostic operator-facing
///     services (lease force-release, scope statistics) neither of those two methods owns.
/// </summary>
public static class ServiceCollectionDhcpServerCoreExtension
{
    /// <summary>
    ///     This method registers <see cref="DhcpServerOptions" /> and every real DHCPv4 engine
    ///     service (<see cref="IDhcpv4ScopeResolver" />, <see cref="IDhcpv4LeaseAllocationService" />,
    ///     <see cref="IDhcpv4OptionOverlayService" />, <see cref="IDhcpServerIdentifierProvider" />),
    ///     and the real
    ///     <see cref="IDhcpv4RequestHandler" /> implementation (<see cref="Dhcpv4RequestHandler" />)
    ///     <see cref="Dhcpv4UdpListener" /> depends on.
    ///     <para>
    ///         Callers must invoke a DHCP repository registration method
    ///         (<c>AddNinlilDhcpEntityFrameworkCore</c> or <c>AddNinlilDhcpMongoDb</c>) before this
    ///         one, since every service registered here depends directly on
    ///         <c>Ninlil.Dhcp.Data.Abstractions</c>'s repository interfaces.
    ///     </para>
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The application configuration to read this server's own core settings from.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDhcpServerCore(
        this IServiceCollection instance,
        IConfiguration configuration
    )
    {
        instance.Configure<DhcpServerOptions>(
            configuration.GetSection(DhcpServerOptions.SectionName)
        );

        instance.AddSingleton<IDhcpServerIdentifierProvider, DhcpServerIdentifierProvider>();
        instance.AddScoped<IDhcpv4ScopeResolver, Dhcpv4ScopeResolver>();
        instance.AddScoped<IDhcpv4LeaseAllocationService, Dhcpv4LeaseAllocationService>();
        instance.AddScoped<IDhcpv4OptionOverlayService, Dhcpv4OptionOverlayService>();

        // Singleton, not scoped: Dhcpv4UdpListener (a process-lifetime BackgroundService)
        // resolves this exactly once, at startup -- see Dhcpv4RequestHandler's own doc comment
        // for why it instead creates its own fresh dependency injection scope on every single
        // HandleRequestAsync call to reach the two scoped services just registered above, rather
        // than this registration needing to be scoped itself.
        instance.AddSingleton<IDhcpv4RequestHandler, Dhcpv4RequestHandler>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }

    /// <summary>
    ///     This method registers every real DHCPv6 engine service (<see cref="IDhcpv6ScopeResolver" />,
    ///     <see cref="IDhcpv6LeaseAllocationService" />, <see cref="IDhcpv6ServerIdentifierProvider" />,
    ///     <see cref="IDhcpv6ServerAddressProvider" />, <see cref="IDhcpv6OptionOverlayService" />)
    ///     and the real <see cref="IDhcpv6RequestHandler" />/<see cref="IDhcpv6RelayMessageHandler" />
    ///     implementation (<see cref="Dhcpv6RequestHandler" />, which implements both interfaces
    ///     against the exact same instance) <see cref="Dhcpv6UdpListener" /> depends on -- the
    ///     DHCPv6 analogue of <see cref="AddNinlilDhcpServerCore" />, kept as its own separate
    ///     method (rather than folded into that one) because the two engines are independent,
    ///     orthogonal choices: the composition root always calls exactly one of this method or
    ///     <c>Ninlil.Dhcp.Server.Relay.AddNinlilDhcpv6Relay</c> -- see <c>Dhcpv6RelayOptions</c>'s
    ///     own doc comment for why -- regardless of which of the two mutually exclusive DHCPv4
    ///     engines is active alongside it.
    ///     <para>
    ///         <see cref="DhcpServerOptions" /> itself is not configured again here -- both engines
    ///         share the exact same <c>DhcpServer:Core</c> section, and
    ///         <see cref="AddNinlilDhcpServerCore" /> already binds it; calling both methods (the
    ///         composition root's own real, current order) configures it twice harmlessly, but a
    ///         caller invoking only this method on its own must still configure
    ///         <see cref="DhcpServerOptions" /> itself first.
    ///     </para>
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The application configuration to read this server's own core settings from.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDhcpv6ServerCore(
        this IServiceCollection instance,
        IConfiguration configuration
    )
    {
        instance.Configure<DhcpServerOptions>(
            configuration.GetSection(DhcpServerOptions.SectionName)
        );

        instance.AddSingleton<IDhcpv6ServerIdentifierProvider, Dhcpv6ServerIdentifierProvider>();
        instance.AddSingleton<IDhcpv6ServerAddressProvider, Dhcpv6ServerAddressProvider>();
        instance.AddScoped<IDhcpv6ScopeResolver, Dhcpv6ScopeResolver>();
        instance.AddScoped<IDhcpv6LeaseAllocationService, Dhcpv6LeaseAllocationService>();
        instance.AddScoped<IDhcpv6OptionOverlayService, Dhcpv6OptionOverlayService>();

        // Registered once as its own concrete type, then wired to both interfaces it implements
        // against that exact same singleton instance -- Dhcpv6UdpListener resolves
        // IDhcpv6RequestHandler for an ordinary client message, and (optionally, see that class's
        // own doc comment) IDhcpv6RelayMessageHandler for a RELAY-FORW an external relay agent
        // forwarded here, and both must reach the same instance rather than two independently
        // constructed ones.
        instance.AddSingleton<Dhcpv6RequestHandler>();
        instance.AddSingleton<IDhcpv6RequestHandler>(sp =>
            sp.GetRequiredService<Dhcpv6RequestHandler>()
        );
        instance.AddSingleton<IDhcpv6RelayMessageHandler>(sp =>
            sp.GetRequiredService<Dhcpv6RequestHandler>()
        );

        // We're done, return the IServiceCollection instance.
        return instance;
    }

    /// <summary>
    ///     This method registers <see cref="IDhcpLeaseAdministrationService" /> and
    ///     <see cref="IDhcpScopeStatisticsService" /> -- the two operator-facing, protocol-agnostic
    ///     DHCP services that operate purely against <c>Ninlil.Dhcp.Data.Abstractions</c>'s own
    ///     repositories (never against a specific DHCPv4/DHCPv6 engine instance), so this method is
    ///     deliberately kept separate from <see cref="AddNinlilDhcpServerCore" />/
    ///     <see cref="AddNinlilDhcpv6ServerCore" /> and is meant to be called unconditionally by the
    ///     composition root -- see each service's own doc comment for why. Requires the same DHCP
    ///     repository registration precondition (<c>AddNinlilDhcpEntityFrameworkCore</c> or
    ///     <c>AddNinlilDhcpMongoDb</c>) those two methods already document.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDhcpLeaseAdministration(
        this IServiceCollection instance
    )
    {
        instance.AddScoped<IDhcpLeaseAdministrationService, DhcpLeaseAdministrationService>();
        instance.AddScoped<IDhcpScopeStatisticsService, DhcpScopeStatisticsService>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
