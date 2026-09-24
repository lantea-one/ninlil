using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Common.Control;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Tftp.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     Ninlil's own read-only TFTP server.
/// </summary>
public static class ServiceCollectionTftpExtension
{
    /// <summary>
    ///     This method registers <see cref="TftpServerOptions" /> and <see cref="TftpServer" /> as
    ///     a singleton -- both under its own concrete type and as
    ///     <see cref="IDhcpProtocolService" />, so a protocol-agnostic caller can reach it without
    ///     knowing it is specifically the TFTP server -- plus <see cref="TftpServerHostedService" />,
    ///     the thin <see cref="Microsoft.Extensions.Hosting.IHostedService" /> wrapper that starts
    ///     it automatically at host boot only when <see cref="TftpServerOptions.Enable" /> is
    ///     configured <see langword="true" /> -- see that class's own doc comment for why the
    ///     server itself stays independently controllable for the rest of the process's life
    ///     regardless of that flag.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The application configuration to read this server's own settings from.</param>
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
