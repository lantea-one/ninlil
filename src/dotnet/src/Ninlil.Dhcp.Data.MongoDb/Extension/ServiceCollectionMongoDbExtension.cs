using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dhcp.Data.MongoDb.Repository;

// Define our namespace.
namespace Ninlil.Dhcp.Data.MongoDb.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up the DHCP
///     repositories on top of the MongoDB persistence layer.
/// </summary>
public static class ServiceCollectionMongoDbExtension
{
    /// <summary>
    ///     This method registers the six DHCP repositories as scoped services. Unlike
    ///     <c>Ninlil.Dns.Data.MongoDb.Extension.ServiceCollectionMongoDbExtension
    ///     .AddNinlilDnsMongoDb</c>, it takes no
    ///     <see cref="Microsoft.Extensions.Configuration.IConfiguration" /> and registers neither
    ///     the MongoDB client, <c>NinlilDnsMongoContext</c>, the change tracker nor the unit of
    ///     work: the DHCP collections live on that shared context, so the caller must call that
    ///     method first.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDhcpMongoDb(this IServiceCollection instance)
    {
        instance.AddScoped<IDhcpScopeRepository, DhcpScopeMongoRepository>();
        instance.AddScoped<IDhcpReservationRepository, DhcpReservationMongoRepository>();
        instance.AddScoped<ILeaseRepository, LeaseMongoRepository>();
        instance.AddScoped<
            IDhcpScopeCustomOptionRepository,
            DhcpScopeCustomOptionMongoRepository
        >();
        instance.AddScoped<IDhcpTagRuleRepository, DhcpTagRuleMongoRepository>();
        instance.AddScoped<IDhcpTagRuleOptionRepository, DhcpTagRuleOptionMongoRepository>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
