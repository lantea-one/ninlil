using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;
using Ninlil.Web.Options;

// Define our namespace.
namespace Ninlil.Web.Startup;

/// <summary>
///     This static class makes the runtime configuration file
///     (<see cref="RuntimeConfigurationOptions.ConfigurationFilePath" />) a configuration source
///     that reloads on change. The settings pages merge their writes into that file, and code that
///     reads a setting through <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}" />
///     only sees a write once a source reloads it. <c>WebApplication.CreateBuilder</c> already
///     loads <c>appsettings.json</c> that way, so the default path needs nothing; a custom path is
///     a file nothing else loads, and <see cref="AddIfMissing" /> adds it. <c>Program.cs</c> calls
///     it right after the <c>NINLIL_*</c> environment variable source is in place.
/// </summary>
public static class RuntimeConfigurationFileSource
{
    /// <summary>
    ///     This method returns the configured runtime configuration file path, or
    ///     <c>appsettings.json</c> in the content root when none is set.
    /// </summary>
    /// <param name="configuration">The configuration that holds the setting.</param>
    /// <param name="contentRootPath">The content root the default file lives in.</param>
    /// <returns>The path as configured, not yet made absolute.</returns>
    public static string ResolvePath(IConfiguration configuration, string contentRootPath)
    {
        string configured = configuration[
            $"{RuntimeConfigurationOptions.SectionName}:{nameof(RuntimeConfigurationOptions.ConfigurationFilePath)}"
        ];

        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(contentRootPath, "appsettings.json")
            : configured;
    }

    /// <summary>
    ///     This method adds the file as an optional, reload-on-change JSON source at
    ///     <paramref name="insertIndex" />, unless a JSON source already loads that exact file.
    ///     Insertion before the environment variable source keeps the precedence of
    ///     <c>appsettings.json</c>: an environment variable overrides a saved value. The file is
    ///     optional because it does not exist until the first save.
    /// </summary>
    /// <param name="sources">The builder's source list.</param>
    /// <param name="path">The runtime configuration file path.</param>
    /// <param name="insertIndex">The position to insert at; clamped to the list.</param>
    /// <returns><see langword="true" /> when a source was added.</returns>
    public static bool AddIfMissing(
        IList<IConfigurationSource> sources,
        string path,
        int insertIndex
    )
    {
        string fullPath = Path.GetFullPath(path);
        foreach (IConfigurationSource existing in sources)
        {
            if (existing is JsonConfigurationSource json && IsSameFile(json, fullPath))
                return false;
        }

        JsonConfigurationSource source = new()
        {
            Path = fullPath,
            Optional = true,
            ReloadOnChange = true,
        };

        // Without a provider the source would resolve one lazily against the builder's base path;
        // doing it here pins it to the file's own directory, which may not exist yet.
        source.ResolveFileProvider();
        sources.Insert(Math.Clamp(insertIndex, 0, sources.Count), source);
        return true;
    }

    /// <summary>
    ///     This method tells whether a JSON source loads the file at the given full path. A path
    ///     differs in case only on a case-insensitive file system, so Linux compares exactly and
    ///     the other systems ignore case.
    /// </summary>
    private static bool IsSameFile(JsonConfigurationSource source, string fullPath)
    {
        if (string.IsNullOrEmpty(source.Path))
            return false;

        string sourcePath = source.Path;
        if (!Path.IsPathRooted(sourcePath))
        {
            // A relative path is relative to the source's file provider root; a source without a
            // physical provider cannot be placed, so it never matches.
            if (source.FileProvider is not PhysicalFileProvider physical)
                return false;

            sourcePath = Path.Combine(physical.Root, sourcePath);
        }

        return string.Equals(
            Path.GetFullPath(sourcePath),
            fullPath,
            OperatingSystem.IsLinux()
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase
        );
    }
}
