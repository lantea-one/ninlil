using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;

// Define our namespace.
namespace Ninlil.Web.Services.Storage;

/// <summary>
///     This record says which configuration source decides <c>Storage:Engine</c> and
///     <c>Storage:ConnectionString</c>.
/// </summary>
/// <param name="IsRuntimeFile">
///     True when the runtime configuration file decides, so Ninlil can rewrite the two settings
///     itself. False when another source wins and the operator must change it.
/// </param>
/// <param name="SourceName">A short name of the deciding source, for messages.</param>
public sealed record StorageConfigurationOriginResult(bool IsRuntimeFile, string SourceName);

/// <summary>
///     This class finds which configuration source decides the storage settings. Environment
///     variables and later sources override the runtime configuration file, so a storage switch
///     can only rewrite the settings itself when that file wins.
///     <see cref="StorageEngineSwitchService" /> uses it to tell the operator what to do after a
///     request, and <see cref="PendingStorageSwitchApplier" /> uses it to decide whether it may
///     write the file at the next start.
/// </summary>
public static class StorageConfigurationOrigin
{
    private const string EngineKey = "Storage:Engine";
    private const string ConnectionStringKey = "Storage:ConnectionString";

    /// <summary>
    ///     This method walks the configuration providers from the last added (highest priority) to
    ///     the first and returns the first one that holds either storage key. When no provider
    ///     holds a key, the defaults apply and the runtime file is the origin only if it is a
    ///     loaded JSON source, since a later save to it then takes effect.
    /// </summary>
    /// <param name="configuration">The application's configuration root.</param>
    /// <param name="runtimeConfigurationFilePath">The runtime configuration file's path.</param>
    public static StorageConfigurationOriginResult Resolve(
        IConfigurationRoot configuration,
        string runtimeConfigurationFilePath
    )
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string runtimePath = string.IsNullOrWhiteSpace(runtimeConfigurationFilePath)
            ? null
            : Path.GetFullPath(runtimeConfigurationFilePath);

        foreach (IConfigurationProvider provider in configuration.Providers.Reverse())
        {
            if (!provider.TryGet(EngineKey, out _) && !provider.TryGet(ConnectionStringKey, out _))
                continue;

            return IsRuntimeFile(provider, runtimePath)
                ? new StorageConfigurationOriginResult(true, "the runtime configuration file")
                : new StorageConfigurationOriginResult(false, Describe(provider));
        }

        bool runtimeFileLoaded = configuration.Providers.Any(provider =>
            IsRuntimeFile(provider, runtimePath)
        );
        return runtimeFileLoaded
            ? new StorageConfigurationOriginResult(true, "the runtime configuration file")
            : new StorageConfigurationOriginResult(false, "the built-in defaults");
    }

    /// <summary>
    ///     This method reports whether <paramref name="provider" /> reads the runtime file.
    /// </summary>
    private static bool IsRuntimeFile(IConfigurationProvider provider, string runtimePath)
    {
        if (runtimePath is null || provider is not JsonConfigurationProvider json)
            return false;

        FileConfigurationSource source = json.Source;
        if (string.IsNullOrEmpty(source.Path))
            return false;

        // A file provider turns the relative name into a physical path; without one the path is
        // relative to the process directory.
        string physical =
            source.FileProvider?.GetFileInfo(source.Path)?.PhysicalPath
            ?? Path.GetFullPath(source.Path);

        StringComparison comparison =
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(physical), runtimePath, comparison);
    }

    /// <summary>
    ///     This method names a provider for the operator: the file for a file source, otherwise
    ///     the provider's kind (environment variables, command line, and so on).
    /// </summary>
    private static string Describe(IConfigurationProvider provider)
    {
        if (provider is FileConfigurationProvider file && !string.IsNullOrEmpty(file.Source.Path))
            return $"the configuration file '{file.Source.Path}'";

        string name = provider.GetType().Name;
        if (name.Contains("EnvironmentVariables", StringComparison.Ordinal))
            return "the environment variables";
        if (name.Contains("CommandLine", StringComparison.Ordinal))
            return "the command line";
        return $"the {name.Replace("ConfigurationProvider", "", StringComparison.Ordinal)} configuration source";
    }
}
