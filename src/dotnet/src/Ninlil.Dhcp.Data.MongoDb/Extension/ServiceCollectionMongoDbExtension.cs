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
    ///     This method registers every DHCP repository this project implements. Unlike
    ///     <c>Ninlil.Dns.Data.MongoDb.Extension.ServiceCollectionMongoDbExtension
    ///     .AddNinlilDnsMongoDb</c>, this method takes no
    ///     <see cref="Microsoft.Extensions.Configuration.IConfiguration" /> and does not register
    ///     the MongoDB client, <c>NinlilDnsMongoContext</c>, change tracker, or unit of work
    ///     itself -- DHCP's own data lives in collections on that one already-shared context, so
    ///     the caller must invoke that method first; this one only layers the DHCP-specific
    ///     repositories on top of what it already registered.
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
