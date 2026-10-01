using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Common.Control;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Tftp.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     Ninlil's read-only TFTP server.
/// </summary>
public static class ServiceCollectionTftpExtension
{
    /// <summary>
    ///     This method registers <see cref="TftpServerOptions" />, <see cref="TftpServer" /> as a
    ///     singleton under its concrete type and as <see cref="IDhcpProtocolService" /> (so
    ///     service control can reach it with the other listeners), and
    ///     <see cref="TftpServerHostedService" />, which starts it at boot when
    ///     <see cref="TftpServerOptions.Enable" /> is set.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The application configuration the TFTP settings are read from.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDhcpTftpServer(
        this IServiceCollection instance,
        IConfiguration configuration
    )
    {
        instance.Configure<TftpServerOptions>(
            configuration.GetSection(TftpServerOptions.SectionName)
        );

        instance.AddSingleton<TftpServer>();
        instance.AddSingleton<IDhcpProtocolService>(sp => sp.GetRequiredService<TftpServer>());
        instance.AddHostedService<TftpServerHostedService>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
