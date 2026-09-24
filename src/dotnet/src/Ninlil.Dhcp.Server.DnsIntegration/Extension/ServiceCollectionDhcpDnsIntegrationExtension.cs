using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dhcp.Server.DnsIntegration.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     Ninlil's DHCP-&gt;DNS integration feature.
/// </summary>
public static class ServiceCollectionDhcpDnsIntegrationExtension
{
    /// <summary>
    ///     This method registers <see cref="DhcpDnsRegistrationService" /> and the
    ///     <see cref="DhcpLeaseChangedEvent" /> subscriber that drives it.
    ///     <para>
    ///         The composition root is expected to have already registered
    ///         <c>Ninlil.Dns.Server.Core</c>'s own event bus, a DHCP repository implementation
    ///         (<c>AddNinlilDhcpEntityFrameworkCore</c>/<c>AddNinlilDhcpMongoDb</c>), a DNS
    ///         repository implementation, and <c>Ninlil.Dns.Server.Authoritative</c>'s own
    ///         <c>IZoneUpdateService</c> -- this method only adds this feature's own two services
    ///         on top of all of that, exactly like every other automated DNS-writing feature's own
    ///         registration method in this project (for example
    ///         <c>Ninlil.Dns.Server.Acme.Extension.ServiceCollectionAcmeExtension</c>'s own CAA
    ///         registration).
    ///     </para>
    ///     <para>
    ///         Registered unconditionally, harmlessly, regardless of whether the DHCP engine
    ///         itself is enabled -- this only adds repository-backed service/subscriber wiring,
    ///         never opens a socket or otherwise changes this process's own externally-visible
    ///         behavior, the same reasoning the DHCP repository registrations in
    ///         <c>Ninlil.Web/Program.cs</c> already document for themselves.
    ///     </para>
    ///     <para>
    ///         Also registers <see cref="DhcpClientRegistryAutoPopulationService" /> and its own
    ///         <see cref="DhcpClientRegistryAutoPopulationEventSubscriber" /> -- a sibling
    ///         reaction to the same <see cref="DhcpLeaseChangedEvent" /> the DNS-registration
    ///         subscriber above already handles, auto-populating Ninlil's own known-client
    ///         registry the first time a lease is granted for a client identifier it has never
    ///         seen before. See that service's own doc comment for why this is a separate
    ///         subscriber rather than folding it into the existing one.
    ///     </para>
    ///     <para>
    ///         Also registers <see cref="DhcpLeaseClientIdentityResolver" /> as a singleton --
    ///         its own doc comment explains why this is the right place for it: it needs exactly
    ///         the same precondition (a real, local DHCP lease-granting engine actually running)
    ///         this method's own caller in <c>Ninlil.Web/Program.cs</c> already only invokes this
    ///         method under. It is registered under its own concrete type only, not
    ///         <c>Ninlil.Dns.Server.Core.ClientIdentity.IClientIdentityResolver</c> itself -- see
    ///         <c>CompositeClientIdentityResolver</c>'s own doc comment for why that composition
    ///         decision belongs to the composition root, not to this method.
    ///     </para>
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDhcpDnsIntegration(this IServiceCollection instance)
    {
        // Scoped for the same reason as IZoneUpdateService itself: it depends on the scoped
        // repositories and unit of work the persistence layer registers.
        instance.AddScoped<DhcpDnsRegistrationService>();

        instance.AddScoped<
            IDnsServerEventSubscriber<DhcpLeaseChangedEvent>,
            DhcpLeaseDnsRegistrationEventSubscriber
        >();

        // The known-client registry's own auto-population service and its sibling subscriber
        // against the same event -- see DhcpClientRegistryAutoPopulationService's own doc
        // comment for why this is a second, independent subscriber rather than folding this
        // reaction into DhcpLeaseDnsRegistrationEventSubscriber above.
        instance.AddScoped<DhcpClientRegistryAutoPopulationService>();
        instance.AddScoped<
            IDnsServerEventSubscriber<DhcpLeaseChangedEvent>,
            DhcpClientRegistryAutoPopulationEventSubscriber
        >();

        // Singleton, not scoped: it opens its own scope internally, per call, to reach
        // ILeaseRepository -- the same "singleton wrapping a scope factory" shape
        // DnsFilterEngine's own registration already uses.
        instance.AddSingleton<DhcpLeaseClientIdentityResolver>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
