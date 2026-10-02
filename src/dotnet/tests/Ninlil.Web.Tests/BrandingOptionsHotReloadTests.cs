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
        string path = Path.Combine(
            Path.GetTempPath(),
            $"ninlil-dns-branding-hot-reload-{Guid.NewGuid():N}.json"
        );

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

            TaskCompletionSource reloaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable subscription = monitor.OnChange(_ => reloaded.TrySetResult());

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

            // The JSON configuration provider debounces file-change notifications by a few
            // hundred milliseconds, so wait for the reload event rather than a fixed delay.
            Task completed = await Task.WhenAny(
                reloaded.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            Assert.Same(reloaded.Task, completed);

            // The monitor created before the write now reports both new values.
            Assert.Equal("Acme Corp DNS", monitor.CurrentValue.SiteTitle);
            Assert.Equal(logoDataUri, monitor.CurrentValue.LogoDataUri);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Best-effort cleanup; a leftover temporary file is harmless.
            }
        }
    }
}
