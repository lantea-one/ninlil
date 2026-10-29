using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Identity.Options;

// Define our namespace.
namespace Ninlil.Dns.Identity.Tests;

/// <summary>
///     This class tests that <see cref="IdentityOptionsMonitorProxy" /> lets a new
///     <see cref="UserManager{TUser}" /> see a configuration change without a restart. It uses the
///     .NET configuration and options pipeline with a reload-on-change JSON file in a temporary
///     directory.
///     <para>
///         <see cref="UserManager{TUser}" /> reads <see cref="IOptions{TOptions}" /> of
///         <see cref="IdentityOptions" /> once, at construction. The test copies that pattern
///         (resolve in a new scope, read <c>.Value</c> once, keep the result) rather than reading
///         <see cref="IOptionsMonitor{TOptions}.CurrentValue" />, which would always be current and
///         prove nothing. It checks that a new scope sees the new value while a value captured
///         earlier stays as it was: the change applies from the next request, as the proxy
///         describes.
///     </para>
/// </summary>
public sealed class IdentityOptionsMonitorProxyHotReloadTests
{
    [Fact]
    public async Task ANewlyResolvedAccessor_ObservesALiveConfigurationWrite_WhileAnAlreadyCapturedValueStaysStale()
    {
        // A directory for this test rather than the shared temp directory: the file watcher
        // behind reloadOnChange reports every change in the directory it watches, and on a busy
        // machine unrelated churn there can overflow its queue and drop the change we wait for.
        string directory = Directory
            .CreateTempSubdirectory("ninlil-dns-identity-options-hot-reload-")
            .FullName;
        string path = Path.Combine(directory, "appsettings.json");

        try
        {
            // A reload-on-change JSON file provider, the kind WebApplication.CreateBuilder() uses
            // for appsettings.json.
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddJsonFile(path, optional: true, reloadOnChange: true)
                .Build();

            ServiceCollection services = new();

            // Same order as ServiceCollectionIdentityExtension: AddIdentityCore defaults, the
            // configuration section, then the proxy, registered last so it wins over the
            // framework's TryAdd default.
            services.Configure<IdentityOptions>(options => options.Password.RequiredLength = 12);
            services.Configure<IdentityOptions>(configuration.GetSection("Identity"));
            services.AddSingleton<IOptions<IdentityOptions>>(
                provider => new IdentityOptionsMonitorProxy(
                    provider.GetRequiredService<IOptionsMonitor<IdentityOptions>>()
                )
            );

            await using ServiceProvider provider = services.BuildServiceProvider();

            // Like the UserManager<TUser> constructor: resolve IOptions<IdentityOptions> in a new
            // scope, read .Value once and keep only the result.
            int CaptureRequiredLengthFromAFreshScope()
            {
                using IServiceScope scope = provider.CreateScope();
                IOptions<IdentityOptions> accessor = scope.ServiceProvider.GetRequiredService<
                    IOptions<IdentityOptions>
                >();

                return accessor.Value.Password.RequiredLength;
            }

            // Before any write the file does not exist, so the built-in default applies.
            int capturedBeforeWrite = CaptureRequiredLengthFromAFreshScope();
            Assert.Equal(12, capturedBeforeWrite);

            IOptionsMonitor<IdentityOptions> monitor = provider.GetRequiredService<
                IOptionsMonitor<IdentityOptions>
            >();
            // The same write IdentitySettingsController.Patch uses.
            await Server.Presets.PresetConfigurationFileApplier.ApplyAsync(
                [
                    new Server.Presets.PresetConfigurationSetting(
                        "Identity:Password",
                        "RequiredLength",
                        20
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
                attempt < 200 && !(monitor.CurrentValue.Password.RequiredLength == 20);
                attempt++
            )
                await Task.Delay(50);

            Assert.True(
                monitor.CurrentValue.Password.RequiredLength == 20,
                "The configuration reload did not happen within the timeout."
            );

            // A new scope (the next request) sees the new value without a restart.
            int capturedAfterWrite = CaptureRequiredLengthFromAFreshScope();
            Assert.Equal(20, capturedAfterWrite);

            // The value captured before the write stays as it was: changes apply from the next
            // request, not inside an existing instance.
            Assert.Equal(12, capturedBeforeWrite);
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
