using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ninlil.Dhcp.Protocol.Dhcpv4;
using Ninlil.Dhcp.Protocol.Dhcpv6;
using Ninlil.Dhcp.Server.Core.Administration;
using Ninlil.Dhcp.Server.Core.Allocation;
using Ninlil.Dhcp.Server.Core.DnsUpdate;
using Ninlil.Dhcp.Server.Core.Identity;
using Ninlil.Dhcp.Server.Core.Options;
using Ninlil.Dhcp.Server.Core.Scope;
using Ninlil.Dhcp.Server.Core.Statistics;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up Ninlil's
///     DHCPv4 server engine (<see cref="AddNinlilDhcpServerCore" />), its DHCPv6 server engine
///     (<see cref="AddNinlilDhcpv6ServerCore" />) and the two engine-independent services for
///     operators, lease force-release and scope statistics
///     (<see cref="AddNinlilDhcpLeaseAdministration" />). <c>Program.cs</c> calls each engine
///     method only when that protocol's relay agent is not enabled.
/// </summary>
public static class ServiceCollectionDhcpServerCoreExtension
{
    /// <summary>
    ///     This method binds <see cref="DhcpServerOptions" /> and registers the DHCPv4 engine:
    ///     <see cref="IDhcpServerIdentifierProvider" /> (singleton), the scoped
    ///     <see cref="IDhcpv4ScopeResolver" />, <see cref="IDhcpv4LeaseAllocationService" /> and
    ///     <see cref="IDhcpv4OptionOverlayService" />, the default
    ///     <see cref="IDhcpDnsUpdatePolicy" />, and <see cref="Dhcpv4RequestHandler" /> as the
    ///     singleton <see cref="IDhcpv4RequestHandler" /> that <see cref="Dhcpv4UdpListener" />
    ///     calls.
    ///     <para>
    ///         The DHCP repositories (<c>AddNinlilDhcpEntityFrameworkCore</c> or
    ///         <c>AddNinlilDhcpMongoDb</c>) must also be registered, since these services depend on
    ///         them.
    ///     </para>
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The application configuration to bind <see cref="DhcpServerOptions" /> from.</param>
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
        // The default policy registers no host names; AddNinlilDhcpDnsIntegration replaces it.
        instance.TryAddScoped<IDhcpDnsUpdatePolicy, NoDhcpDnsUpdatePolicy>();

        // Singleton, because the singleton Dhcpv4UdpListener holds it. Dhcpv4RequestHandler
        // creates a scope per request to reach the scoped services above.
        instance.AddSingleton<IDhcpv4RequestHandler, Dhcpv4RequestHandler>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }

    /// <summary>
    ///     This method binds <see cref="DhcpServerOptions" /> and registers the DHCPv6 engine: the
    ///     singleton <see cref="IDhcpv6ServerIdentifierProvider" /> and
    ///     <see cref="IDhcpv6ServerAddressProvider" />, the scoped
    ///     <see cref="IDhcpv6ScopeResolver" />, <see cref="IDhcpv6LeaseAllocationService" /> and
    ///     <see cref="IDhcpv6OptionOverlayService" />, the default
    ///     <see cref="IDhcpDnsUpdatePolicy" />, and one singleton
    ///     <see cref="Dhcpv6RequestHandler" /> serving as both <see cref="IDhcpv6RequestHandler" />
    ///     and <see cref="IDhcpv6RelayMessageHandler" /> for <see cref="Dhcpv6UdpListener" />.
    ///     <para>
    ///         It is separate from <see cref="AddNinlilDhcpServerCore" /> because the two choices
    ///         are independent: <c>Program.cs</c> calls either this method or
    ///         <c>Ninlil.Dhcp.Server.Relay.AddNinlilDhcpv6Relay</c>, whichever DHCPv4 engine is
    ///         active. Both engine methods bind the shared <c>DhcpServer:Core</c> section; binding
    ///         it twice is harmless.
    ///     </para>
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The application configuration to bind <see cref="DhcpServerOptions" /> from.</param>
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
        // The default policy registers no host names; AddNinlilDhcpDnsIntegration replaces it.
        instance.TryAddScoped<IDhcpDnsUpdatePolicy, NoDhcpDnsUpdatePolicy>();

        // One instance behind both interfaces: Dhcpv6UdpListener uses IDhcpv6RequestHandler for
        // client messages and the optional IDhcpv6RelayMessageHandler for RELAY-FORW, and both
        // must reach the same handler.
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
    ///     This method registers the scoped <see cref="IDhcpLeaseAdministrationService" /> and
    ///     <see cref="IDhcpScopeStatisticsService" />. They use only the DHCP repositories, not an
    ///     engine, so <c>Program.cs</c> calls this method in every configuration, separately from
    ///     <see cref="AddNinlilDhcpServerCore" /> and <see cref="AddNinlilDhcpv6ServerCore" />. The
    ///     DHCP repositories must also be registered.
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
