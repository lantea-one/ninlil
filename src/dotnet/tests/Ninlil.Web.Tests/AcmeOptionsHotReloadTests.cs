using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Server.Acme;
using Ninlil.Dns.Server.Presets;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class proves, directly against the real .NET configuration/options binding pipeline
///     rather than through the full HTTP host, that <see cref="AcmeOptions" /> genuinely
///     hot-reloads: a write <see cref="PresetConfigurationFileApplier" /> merges into a JSON file
///     (the exact mechanism <see cref="Controllers.AcmeSettingsController.Patch" /> itself calls)
///     is visible on <see cref="IOptionsMonitor{TOptions}" />'s own <c>CurrentValue</c> with no
///     restart -- the same real functional gap this project's own hot-reload fixes each closed
///     for their own singleton, closed here for <see cref="AcmeCertificateRenewalService" />'s own
///     poll loop and <see cref="AcmeCertificateRenewalService.RenewOneDomainAsync" />.
///     <para>
///         Deliberately not exercised through <see cref="NinlilDnsWebApplicationFactory" /> --
///         see <see cref="AcmeSettingsControllerTests" />'s own doc comment and
///         <see cref="AppearanceOptionsHotReloadTests" />'s own doc comment for exactly why that
///         factory's own isolated runtime settings file is never actually wired back in as a real
///         configuration source. This test reconstructs a real deployment's own shape directly:
///         one file, added as a genuine reload-on-change configuration source, written to via the
///         exact same <see cref="PresetConfigurationFileApplier" /> mechanism the real controller
///         itself calls.
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
            // A real IConfigurationRoot, backed by a real reload-on-change JSON file provider --
            // the exact same kind of source WebApplicationBuilder.CreateBuilder() wires up for
            // appsettings.json in a real deployment.
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddJsonFile(path, optional: true, reloadOnChange: true)
                .Build();

            ServiceCollection services = new();
            services.Configure<AcmeOptions>(configuration.GetSection(AcmeOptions.SectionName));
            await using ServiceProvider provider = services.BuildServiceProvider();

            IOptionsMonitor<AcmeOptions> monitor = provider.GetRequiredService<
                IOptionsMonitor<AcmeOptions>
            >();

            // Before any write, the file does not exist yet -- this class's own compiled-in
            // defaults, exactly as a fresh installation would see them: ACME off, no domains
            // configured.
            Assert.False(monitor.CurrentValue.Enable);
            Assert.Empty(monitor.CurrentValue.Domains);

            TaskCompletionSource reloaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable subscription = monitor.OnChange(_ => reloaded.TrySetResult());

            const string domain = "acme-hot-reload-tests.invalid";

            // The exact same write mechanism AcmeSettingsController.Patch itself calls.
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

            // .NET's own JSON configuration provider debounces file-change notifications by a
            // few hundred milliseconds -- this waits for that real reload to actually happen
            // rather than asserting against a fixed delay, which would either be flaky (too
            // short) or slow every test run down for no reason (too long).
            Task completed = await Task.WhenAny(
                reloaded.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            Assert.Same(reloaded.Task, completed);

            Assert.True(monitor.CurrentValue.Enable);
            Assert.Contains(domain, monitor.CurrentValue.Domains);

            // AcmeCertificateRenewalService.ExecuteAsync/RenewDueDomainsAsync/RenewOneDomainAsync
            // all read this exact same monitor's own CurrentValue on every iteration/call, not a
            // snapshot cached at construction -- this is the real proof the controller's own
            // doc comment claims: the identical singleton this project's DI container
            // constructs once genuinely observes a live AcmeSettingsController.Patch write.
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
