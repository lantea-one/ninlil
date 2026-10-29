using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Presets;
using Ninlil.Dns.Server.QueryLog;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class checks that <see cref="QueryLoggingOptions.PrivacyLevel" />, the one
///     hot-reloading field of <see cref="QueryLoggingOptions" />, hot-reloads: a write that
///     <see cref="PresetConfigurationFileApplier" /> merges into a JSON file shows up on
///     <see cref="IOptionsMonitor{TOptions}.CurrentValue" /> without a restart. It builds its own
///     configuration root with one reload-on-change file, as
///     <see cref="AppearanceOptionsHotReloadTests" /> does and explains.
/// </summary>
public class QueryLoggingOptionsHotReloadTests
{
    [Fact]
    public async Task AWriteThroughPresetConfigurationFileApplier_IsVisibleOnIOptionsMonitorWithNoRestart()
    {
        // A directory for this test rather than the shared temp directory: the file watcher
        // behind reloadOnChange sees every change in the directory it watches, and on a busy
        // machine unrelated churn there can overflow its queue, which makes it report a change
        // to every watched file at once.
        string directory = Directory
            .CreateTempSubdirectory("ninlil-dns-querylogging-hot-reload-")
            .FullName;
        string path = Path.Combine(directory, "appsettings.json");

        try
        {
            // A reload-on-change JSON file source, the kind WebApplicationBuilder.CreateBuilder()
            // adds for appsettings.json.
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

            // The file does not exist yet, so this is the compiled-in default a fresh
            // installation sees: full detail.
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

            // The JSON configuration provider debounces file-change notifications by a few hundred
            // milliseconds. Wait up to ten seconds for the written value itself, not for the first
            // change notification or a fixed delay: a spurious notification can trigger an earlier
            // reload that still reads the old file.
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
                // Best-effort cleanup; a leftover temporary directory is harmless.
            }
        }
    }
}
