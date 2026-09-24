using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Options;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class proves, directly against the real .NET configuration/options binding pipeline
///     rather than through the full HTTP host, that <see cref="AppearanceOptions" /> genuinely
///     hot-reloads: a write <see cref="PresetConfigurationFileApplier" /> merges into a JSON file
///     is visible on <see cref="IOptionsMonitor{TOptions}" />'s own <c>CurrentValue</c> with no
///     restart, because that file is registered as a real, reload-on-change configuration source
///     and read back via <see cref="IOptionsMonitor{TOptions}" /> rather than the usual,
///     snapshotted <see cref="IOptions{TOptions}" />.
///     <para>
///         This is deliberately not exercised through <see cref="NinlilDnsWebApplicationFactory" />
///         (see <see cref="AppearanceSettingsControllerTests" />'s own doc comment): that factory's
///         own isolated runtime settings file is set only as
///         <c>RuntimeConfigurationOptions.ConfigurationFilePath</c> (where a write lands), never
///         wired back in as an actual configuration source the running host's own
///         <see cref="IConfiguration" /> reads from -- deliberately, so a test mutating a runtime
///         setting can never leak into the real, shared build-output <c>appsettings.json</c> every
///         other test class in that shared fixture also runs against. In a real deployment, by
///         contrast, <c>RuntimeConfigurationOptions.ConfigurationFilePath</c> defaults to that
///         exact same <c>appsettings.json</c> -- the same file the host's own configuration
///         pipeline already loads with reload-on-change enabled -- so the two are, in practice,
///         one and the same file, and this test reconstructs precisely that real-world shape
///         directly: one file, added as a genuine reload-on-change configuration source, written
///         to via the exact same <see cref="PresetConfigurationFileApplier" /> mechanism
///         <see cref="Controllers.AppearanceSettingsController.Patch" /> itself calls.
///     </para>
/// </summary>
public class AppearanceOptionsHotReloadTests
{
    [Fact]
    public async Task AWriteThroughPresetConfigurationFileApplier_IsVisibleOnIOptionsMonitorWithNoRestart()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"ninlil-dns-appearance-hot-reload-{Guid.NewGuid():N}.json"
        );

        try
        {
            // A real IConfigurationRoot, backed by a real reload-on-change JSON file provider --
            // the exact same kind of source WebApplicationBuilder.CreateBuilder() wires up for
            // appsettings.json in a real deployment.
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

            // Before any write, the file does not exist yet -- this class's own compiled-in
            // defaults, exactly as a fresh installation would see them.
            Assert.Equal(UiThemePaletteEnum.Teal, monitor.CurrentValue.Palette);
            Assert.Equal(UiThemeModeEnum.System, monitor.CurrentValue.Mode);

            TaskCompletionSource reloaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable subscription = monitor.OnChange(_ => reloaded.TrySetResult());

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

            // .NET's own JSON configuration provider debounces file-change notifications by a
            // few hundred milliseconds -- this waits for that real reload to actually happen
            // rather than asserting against a fixed delay, which would either be flaky (too
            // short) or slow every test run down for no reason (too long).
            Task completed = await Task.WhenAny(
                reloaded.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            Assert.Same(reloaded.Task, completed);

            Assert.Equal(UiThemePaletteEnum.Indigo, monitor.CurrentValue.Palette);
            Assert.Equal(UiThemeModeEnum.Dark, monitor.CurrentValue.Mode);
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
