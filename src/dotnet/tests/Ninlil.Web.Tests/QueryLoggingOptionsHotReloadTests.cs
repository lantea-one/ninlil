using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Presets;
using Ninlil.Dns.Server.QueryLog;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class proves, directly against the real .NET configuration/options binding pipeline
///     rather than through the full HTTP host, that <see cref="QueryLoggingOptions.PrivacyLevel" />
///     genuinely hot-reloads: a write <see cref="PresetConfigurationFileApplier" /> merges into a
///     JSON file is visible on <see cref="IOptionsMonitor{TOptions}" />'s own
///     <c>CurrentValue</c> with no restart, mirroring <see cref="AppearanceOptionsHotReloadTests" />'s
///     own identical shape and reasoning for <c>AppearanceOptions</c> -- the same real, reload-
///     on-change configuration source, the same real merge-patch write mechanism, just against a
///     different options class's own one hot-reloading field.
/// </summary>
public class QueryLoggingOptionsHotReloadTests
{
    [Fact]
    public async Task AWriteThroughPresetConfigurationFileApplier_IsVisibleOnIOptionsMonitorWithNoRestart()
    {
        // A directory of this test's own, rather than the shared temp directory itself: the file
        // watcher behind reloadOnChange sees every change in the directory it watches, and on a
        // busy machine unrelated churn there can overflow its queue, which makes it report a
        // change to every watched file at once.
        string directory = Directory
            .CreateTempSubdirectory("ninlil-dns-querylogging-hot-reload-")
            .FullName;
        string path = Path.Combine(directory, "appsettings.json");

        try
        {
            // A real IConfigurationRoot, backed by a real reload-on-change JSON file provider --
            // the exact same kind of source WebApplicationBuilder.CreateBuilder() wires up for
            // appsettings.json in a real deployment.
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddJsonFile(path, optional: true, reloadOnChange: true)
                .Build();

            ServiceCollection services = new();
            services.Configure<QueryLoggingOptions>(
                configuration.GetSection(QueryLoggingOptions.SectionName)
            );
            await using ServiceProvider provider = services.BuildServiceProvider();

            IOptionsMonitor<QueryLoggingOptions> monitor = provider.GetRequiredService<
                IOptionsMonitor<QueryLoggingOptions>
            >();

            // Before any write, the file does not exist yet -- this class's own compiled-in
            // default, exactly as a fresh installation would see it: full detail, unchanged from
            // before this privacy scale even existed.
            Assert.Equal(QueryLogPrivacyLevelEnum.Full, monitor.CurrentValue.PrivacyLevel);

            await PresetConfigurationFileApplier.ApplyAsync(
                [
                    new PresetConfigurationSetting(
                        QueryLoggingOptions.SectionName,
                        nameof(QueryLoggingOptions.PrivacyLevel),
                        QueryLogPrivacyLevelEnum.FullyAnonymous.ToString()
                    ),
                ],
                path
            );

            // .NET's own JSON configuration provider debounces file-change notifications by a
            // few hundred milliseconds -- this waits (up to ten seconds) for the written value to
            // actually arrive, rather than for the first change notification or a fixed delay: a
            // spurious notification can trigger an earlier reload that still reads the old file.
            for (
                int attempt = 0;
                attempt < 200
                    && monitor.CurrentValue.PrivacyLevel != QueryLogPrivacyLevelEnum.FullyAnonymous;
                attempt++
            )
                await Task.Delay(50);

            Assert.Equal(
                QueryLogPrivacyLevelEnum.FullyAnonymous,
                monitor.CurrentValue.PrivacyLevel
            );
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup only -- a lingering temp file from a test run is harmless.
            }
        }
    }
}
