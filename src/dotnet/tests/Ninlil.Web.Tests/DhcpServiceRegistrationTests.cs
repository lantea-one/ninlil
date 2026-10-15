using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Server.DnsIntegration;
using Ninlil.Dhcp.Server.Relay;
using Ninlil.Dns.Server.ClientIdentity;
using Ninlil.Dns.Server.Core.ClientIdentity;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Web.Startup;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="DhcpServiceRegistration" /> on a plain
///     <see cref="ServiceCollection" /> with in-memory configuration (no web host): the
///     DHCP-to-DNS integration and the DHCP-lease client identity resolver are registered whatever
///     the two relay flags say, since DHCPv6 leases come from the local engine even when DHCPv4 is
///     relayed. The identity chain is observed by building it from stand-in resolvers and reading
///     the composite's resolver list.
/// </summary>
public class DhcpServiceRegistrationTests
{
    /// <summary>
    ///     This method builds the collection for one combination of relay flags.
    /// </summary>
    private static ServiceCollection Build(bool dhcpv4Relay, bool dhcpv6Relay)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string>
                {
                    [$"{Dhcpv4RelayOptions.SectionName}:Enable"] = dhcpv4Relay.ToString(),
                    [$"{Dhcpv6RelayOptions.SectionName}:Enable"] = dhcpv6Relay.ToString(),
                }
            )
            .Build();

        ServiceCollection services = new();
        services.AddNinlilDhcpEngines(configuration);
        services.AddNinlilClientIdentityResolverChain();
        return services;
    }

    /// <summary>
    ///     This method returns the types of the resolvers the composite chain holds, in order.
    /// </summary>
    private static List<Type> ChainTypes(ServiceCollection services)
    {
        // The chain asks for each resolver by its concrete type; sealed classes with heavy
        // constructors are stood in by uninitialised instances that are never called.
        foreach (
            Type type in new[]
            {
                typeof(StaticAddressMappingClientIdentityResolver),
                typeof(DhcpLeaseClientIdentityResolver),
                typeof(ArpNeighborTableClientIdentityResolver),
            }
        )
            services.AddSingleton(type, RuntimeHelpers.GetUninitializedObject(type));

        using ServiceProvider provider = services.BuildServiceProvider();
        object composite = provider.GetRequiredService<IClientIdentityResolver>();
        Assert.IsType<CompositeClientIdentityResolver>(composite);

        // The list is a primary-constructor parameter, so the compiler names the field itself.
        FieldInfo field = typeof(CompositeClientIdentityResolver)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate =>
                candidate.FieldType == typeof(IReadOnlyList<IClientIdentityResolver>)
            );
        return ((IReadOnlyList<IClientIdentityResolver>)field.GetValue(composite))
            .Select(resolver => resolver.GetType())
            .ToList();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void AddNinlilDhcpEngines_InEveryRelayConfiguration_RegistersTheDnsIntegration(
        bool dhcpv4Relay,
        bool dhcpv6Relay
    )
    {
        ServiceCollection services = Build(dhcpv4Relay, dhcpv6Relay);

        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType == typeof(DhcpDnsRegistrationService)
                && descriptor.ImplementationType == typeof(DhcpDnsRegistrationService)
        );
        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType == typeof(IDnsServerEventSubscriber<DhcpLeaseChangedEvent>)
                && descriptor.ImplementationType == typeof(DhcpLeaseDnsRegistrationEventSubscriber)
        );
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void AddNinlilClientIdentityResolverChain_InEveryRelayConfiguration_IncludesTheLeaseResolver(
        bool dhcpv4Relay,
        bool dhcpv6Relay
    )
    {
        List<Type> chain = ChainTypes(Build(dhcpv4Relay, dhcpv6Relay));

        Assert.Equal(
            [
                typeof(StaticAddressMappingClientIdentityResolver),
                typeof(DhcpLeaseClientIdentityResolver),
                typeof(ArpNeighborTableClientIdentityResolver),
            ],
            chain
        );
    }
}
