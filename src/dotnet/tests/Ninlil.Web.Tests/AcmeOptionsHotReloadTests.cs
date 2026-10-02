using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Server.Acme;
using Ninlil.Dns.Server.Presets;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class checks that <see cref="AcmeOptions" /> hot-reloads: a write that
///     <see cref="PresetConfigurationFileApplier" /> merges into a JSON file (the call
///     <see cref="Controllers.AcmeSettingsController.Patch" /> makes) shows up on
///     <see cref="IOptionsMonitor{TOptions}.CurrentValue" /> without a restart.
///     <see cref="AcmeCertificateRenewalService" /> reads <c>CurrentValue</c> on each poll and
///     each renewal, so a live write reaches it; the service itself is not run here.
///     <para>
///         The test builds a configuration root and service collection of its own instead of
///         using <see cref="NinlilDnsWebApplicationFactory" />, whose runtime settings file is not
///         loaded back as a configuration source (see <see cref="AppearanceOptionsHotReloadTests" />).
///         Like a deployment, it has one JSON file added as a reload-on-change source and written
///         through <see cref="PresetConfigurationFileApplier" />.
///     </para>
/// </summary>
public class AcmeOptionsHotReloadTests
{
    [Fact]
    public async Task AWriteThroughPresetConfigurationFileApplier_IsVisibleOnIOptionsMonitorWithNoRestart()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"ninlil-dns-acme-hot-reload-{Guid.NewGuid():N}.json"
        );

        try
        {
            // A reload-on-change JSON file source, the kind WebApplicationBuilder.CreateBuilder()
            // adds for appsettings.json.
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddJsonFile(path, optional: true, reloadOnChange: true)
                .Build();

            ServiceCollection services = new();
            services.Configure<AcmeOptions>(configuration.GetSection(AcmeOptions.SectionName));
            await using ServiceProvider provider = services.BuildServiceProvider();

            IOptionsMonitor<AcmeOptions> monitor = provider.GetRequiredService<
                IOptionsMonitor<AcmeOptions>
            >();

            // The file does not exist yet, so these are the compiled-in defaults a fresh
            // installation sees: ACME off and no domains.
            Assert.False(monitor.CurrentValue.Enable);
            Assert.Empty(monitor.CurrentValue.Domains);

            TaskCompletionSource reloaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable subscription = monitor.OnChange(_ => reloaded.TrySetResult());

            const string domain = "acme-hot-reload-tests.invalid";

            // The same call AcmeSettingsController.Patch makes.
            await PresetConfigurationFileApplier.ApplyAsync(
                [
                    new PresetConfigurationSetting(
                        AcmeOptions.SectionName,
                        nameof(AcmeOptions.Enable),
                        true
                    ),
                    new PresetConfigurationSetting(
                        AcmeOptions.SectionName,
                        nameof(AcmeOptions.Domains),
                        new List<string> { domain }
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

            Assert.True(monitor.CurrentValue.Enable);
            Assert.Contains(domain, monitor.CurrentValue.Domains);
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
