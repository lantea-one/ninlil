using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Server.Backup.Options;

// Define our namespace.
namespace Ninlil.Dns.Server.Backup.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     Ninlil's full-system backup/restore engine.
/// </summary>
public static class ServiceCollectionBackupExtension
{
    /// <summary>
    ///     This method binds <see cref="BackupOptions" /> and registers <see cref="IBackupService" />.
    ///     There is no switch to turn backup off: an operator should always be able to take a
    ///     backup, whatever else is enabled.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The application configuration to read backup settings from.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDnsServerBackup(
        this IServiceCollection instance,
        IConfiguration configuration
    )
    {
        instance.Configure<BackupOptions>(configuration.GetSection(BackupOptions.SectionName));
        instance.AddScoped<IBackupService, SystemBackupService>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
