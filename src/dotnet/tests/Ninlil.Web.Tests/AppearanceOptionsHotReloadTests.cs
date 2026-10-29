using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Options;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class checks that <see cref="AppearanceOptions" /> hot-reloads: a write that
///     <see cref="PresetConfigurationFileApplier" /> merges into a JSON file shows up on
///     <see cref="IOptionsMonitor{TOptions}.CurrentValue" /> without a restart, because the file
///     is a reload-on-change configuration source and the options are read through
///     <see cref="IOptionsMonitor{TOptions}" /> rather than <see cref="IOptions{TOptions}" />.
///     <para>
///         It builds its own configuration root and service collection instead of using
///         <see cref="NinlilDnsWebApplicationFactory" />. That host only sets its temporary
///         settings file as <c>RuntimeConfigurationOptions.ConfigurationFilePath</c> (where writes
///         land) and never loads it as a configuration source, so test writes cannot leak into the
///         <c>appsettings.json</c> in the build output. In a deployment the path defaults to
///         <c>appsettings.json</c>, which the host already loads with reload-on-change, so writes
///         and reads use one file. This test builds that shape: one file, added as a
///         reload-on-change source and written through <see cref="PresetConfigurationFileApplier" />,
///         the call <see cref="Controllers.AppearanceSettingsController.Patch" /> makes.
///     </para>
/// </summary>
public class AppearanceOptionsHotReloadTests
{
    [Fact]
    public async Task AWriteThroughPresetConfigurationFileApplier_IsVisibleOnIOptionsMonitorWithNoRestart()
    {
        // A directory for this test rather than the shared temp directory: the file watcher
        // behind reloadOnChange reports every change in the directory it watches, and on a busy
        // machine unrelated churn there can overflow its queue and drop the change we wait for.
        string directory = Directory
            .CreateTempSubdirectory("ninlil-dns-appearance-hot-reload-")
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
            services.Configure<AppearanceOptions>(
                configuration.GetSection(AppearanceOptions.SectionName)
            );
            await using ServiceProvider provider = services.BuildServiceProvider();

            IOptionsMonitor<AppearanceOptions> monitor = provider.GetRequiredService<
                IOptionsMonitor<AppearanceOptions>
            >();

            // The file does not exist yet, so these are the compiled-in defaults a fresh
            // installation sees.
            Assert.Equal(UiThemePaletteEnum.Teal, monitor.CurrentValue.Palette);
            Assert.Equal(UiThemeModeEnum.System, monitor.CurrentValue.Mode);

            await PresetConfigurationFileApplier.ApplyAsync(
                [
                    new PresetConfigurationSetting(
                        AppearanceOptions.SectionName,
                        nameof(AppearanceOptions.Palette),
                        UiThemePaletteEnum.Indigo.ToString()
                    ),
                    new PresetConfigurationSetting(
                        AppearanceOptions.SectionName,
                        nameof(AppearanceOptions.Mode),
                        UiThemeModeEnum.Dark.ToString()
                    ),
                ],
                path
            );

            // The JSON configuration provider debounces file-change notifications by a few hundred
            // milliseconds. Wait up to ten seconds for the written value itself, not for the first
            // change notification: a notification can arrive for an earlier state of the file and
            // reload before the final content is visible.
            for (
                int attempt = 0;
                attempt < 200
                    && !(
                        monitor.CurrentValue.Palette == UiThemePaletteEnum.Indigo
                        && monitor.CurrentValue.Mode == UiThemeModeEnum.Dark
                    );
                attempt++
            )
                await Task.Delay(50);

            Assert.True(
                monitor.CurrentValue.Palette == UiThemePaletteEnum.Indigo
                    && monitor.CurrentValue.Mode == UiThemeModeEnum.Dark,
                "The configuration reload did not happen within the timeout."
            );

            Assert.Equal(UiThemePaletteEnum.Indigo, monitor.CurrentValue.Palette);
            Assert.Equal(UiThemeModeEnum.Dark, monitor.CurrentValue.Mode);
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
