using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Options;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class proves, directly against the real .NET configuration/options binding pipeline
///     rather than through the full HTTP host, that <see cref="BrandingOptions" /> genuinely
///     hot-reloads: a write <see cref="PresetConfigurationFileApplier" /> merges into a JSON file
///     (the exact mechanism <see cref="Controllers.BrandingSettingsController.Patch" /> itself
///     calls) is visible on <see cref="IOptionsMonitor{TOptions}" />'s own <c>CurrentValue</c>
///     with no restart -- including a real, well-formed data URI for <see cref="BrandingOptions.LogoDataUri" />,
///     not just a plain string field.
///     <para>
///         This is deliberately not exercised through <see cref="NinlilDnsWebApplicationFactory" />
///         -- see <see cref="AppearanceOptionsHotReloadTests" />'s own doc comment (the same
///         "isolated runtime settings file is never wired back in as a live configuration source"
///         limitation applies here identically) for the full reasoning behind reconstructing the
///         real-deployment shape directly instead: one file, added as a genuine reload-on-change
///         configuration source, written to via the exact same
///         <see cref="PresetConfigurationFileApplier" /> mechanism the real controller calls.
///     </para>
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
            // A real IConfigurationRoot, backed by a real reload-on-change JSON file provider --
            // the exact same kind of source WebApplicationBuilder.CreateBuilder() wires up for
            // appsettings.json in a real deployment.
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

            // Before any write, the file does not exist yet -- this class's own compiled-in
            // defaults, exactly as a fresh installation would see them.
            Assert.Null(monitor.CurrentValue.SiteTitle);
            Assert.Null(monitor.CurrentValue.LogoDataUri);

            TaskCompletionSource reloaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable subscription = monitor.OnChange(_ => reloaded.TrySetResult());

            // The exact same write mechanism BrandingSettingsController.Patch itself calls.
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

            // .NET's own JSON configuration provider debounces file-change notifications by a
            // few hundred milliseconds -- this waits for that real reload to actually happen
            // rather than asserting against a fixed delay.
            Task completed = await Task.WhenAny(
                reloaded.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            Assert.Same(reloaded.Task, completed);

            // A fresh read of the same, already-existing IOptionsMonitor instance -- constructed
            // before the write -- genuinely reflects both new values, with no restart of any kind.
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
                // Best-effort cleanup only -- a lingering temp file from a test run is harmless.
            }
        }
    }
}
