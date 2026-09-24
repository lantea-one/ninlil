using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dhcp.Data.EntityFrameworkCore.Repository;

// Define our namespace.
namespace Ninlil.Dhcp.Data.EntityFrameworkCore.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up the DHCP
///     repositories on top of the Entity Framework Core persistence layer.
/// </summary>
public static class ServiceCollectionEntityFrameworkCoreExtension
{
    /// <summary>
    ///     This method registers every DHCP repository this project implements. Unlike
    ///     <c>Ninlil.Dns.Data.EntityFrameworkCore.Extension.ServiceCollectionEntityFrameworkCoreExtension
    ///     .AddNinlilDnsEntityFrameworkCore</c>, this method takes no
    ///     <see cref="Microsoft.Extensions.Configuration.IConfiguration" /> and does not register
    ///     <c>NinlilDnsContext</c> itself or select a relational provider -- DHCP's own data is
    ///     mapped entity types within that one already-shared model, so the caller must invoke
    ///     that method first; this one only layers the DHCP-specific repositories on top of the
    ///     context and unit of work it already registered.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDhcpEntityFrameworkCore(
        this IServiceCollection instance
    )
    {
        instance.AddScoped<IDhcpScopeRepository, DhcpScopeRepository>();
        instance.AddScoped<IDhcpReservationRepository, DhcpReservationRepository>();
        instance.AddScoped<ILeaseRepository, LeaseRepository>();
        instance.AddScoped<IDhcpScopeCustomOptionRepository, DhcpScopeCustomOptionRepository>();
        instance.AddScoped<IDhcpTagRuleRepository, DhcpTagRuleRepository>();
        instance.AddScoped<IDhcpTagRuleOptionRepository, DhcpTagRuleOptionRepository>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
