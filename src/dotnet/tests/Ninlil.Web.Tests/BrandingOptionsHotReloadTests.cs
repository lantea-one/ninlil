using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Options;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class checks that <see cref="BrandingOptions" /> hot-reloads: a write that
///     <see cref="PresetConfigurationFileApplier" /> merges into a JSON file (the call
///     <see cref="Controllers.BrandingSettingsController.Patch" /> makes) shows up on
///     <see cref="IOptionsMonitor{TOptions}.CurrentValue" /> without a restart, including a
///     well-formed data URI for <see cref="BrandingOptions.LogoDataUri" />. Like
///     <see cref="AppearanceOptionsHotReloadTests" /> (which explains why), it builds its own
///     configuration root with one reload-on-change file instead of using
///     <see cref="NinlilDnsWebApplicationFactory" />.
/// </summary>
public class BrandingOptionsHotReloadTests
{
    [Fact]
    public async Task AWriteThroughPresetConfigurationFileApplier_IsVisibleOnIOptionsMonitorWithNoRestart()
    {
        // A directory for this test rather than the shared temp directory: the file watcher
        // behind reloadOnChange reports every change in the directory it watches, and on a busy
        // machine unrelated churn there can overflow its queue and drop the change we wait for.
        string directory = Directory
            .CreateTempSubdirectory("ninlil-dns-branding-hot-reload-")
            .FullName;
        string path = Path.Combine(directory, "appsettings.json");

        const string logoDataUri =
            "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

        try
        {
            // A reload-on-change JSON file source, the kind WebApplicationBuilder.CreateBuilder()
            // adds for appsettings.json.
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddJsonFile(path, optional: true, reloadOnChange: true)
                .Build();

            ServiceCollection services = new();
            services.Configure<BrandingOptions>(
                configuration.GetSection(BrandingOptions.SectionName)
            );
            await using ServiceProvider provider = services.BuildServiceProvider();

            IOptionsMonitor<BrandingOptions> monitor = provider.GetRequiredService<
                IOptionsMonitor<BrandingOptions>
            >();

            // The file does not exist yet, so these are the compiled-in defaults.
            Assert.Null(monitor.CurrentValue.SiteTitle);
            Assert.Null(monitor.CurrentValue.LogoDataUri);

            // The same call BrandingSettingsController.Patch makes.
            await PresetConfigurationFileApplier.ApplyAsync(
                [
                    new PresetConfigurationSetting(
                        BrandingOptions.SectionName,
                        nameof(BrandingOptions.SiteTitle),
                        "Acme Corp DNS"
                    ),
                    new PresetConfigurationSetting(
                        BrandingOptions.SectionName,
                        nameof(BrandingOptions.LogoDataUri),
                        logoDataUri
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
                        monitor.CurrentValue.SiteTitle == "Acme Corp DNS"
                        && monitor.CurrentValue.LogoDataUri == logoDataUri
                    );
                attempt++
            )
                await Task.Delay(50);

            Assert.True(
                monitor.CurrentValue.SiteTitle == "Acme Corp DNS"
                    && monitor.CurrentValue.LogoDataUri == logoDataUri,
                "The configuration reload did not happen within the timeout."
            );

            // The monitor created before the write now reports both new values.
            Assert.Equal("Acme Corp DNS", monitor.CurrentValue.SiteTitle);
            Assert.Equal(logoDataUri, monitor.CurrentValue.LogoDataUri);
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
