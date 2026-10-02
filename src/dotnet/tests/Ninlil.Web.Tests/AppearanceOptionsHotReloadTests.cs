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
        string path = Path.Combine(
            Path.GetTempPath(),
            $"ninlil-dns-appearance-hot-reload-{Guid.NewGuid():N}.json"
        );

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

            // The JSON configuration provider debounces file-change notifications by a few
            // hundred milliseconds. Waiting for the reload event avoids a fixed delay, which would
            // be either flaky or needlessly slow.
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
                // Best-effort cleanup; a leftover temporary file is harmless.
            }
        }
    }
}
