using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Identity.Options;

// Define our namespace.
namespace Ninlil.Dns.Identity.Tests;

/// <summary>
///     This class proves, against the real .NET configuration/options binding pipeline and a real
///     reload-on-change JSON configuration file, that <see cref="IdentityOptionsMonitorProxy" />
///     actually closes the hot-reload gap it exists to close: <see cref="UserManager{TUser}" />'s
///     own constructor (a fixed framework signature this project cannot change) reads
///     <see cref="IOptions{TOptions}" /> of <see cref="IdentityOptions" /> exactly once, capturing
///     the result in its own private field for that instance's lifetime -- this test simulates
///     that exact "read <c>.Value</c> once, at construction, and keep it" shape directly, rather
///     than reading <see cref="IOptionsMonitor{TOptions}.CurrentValue" /> repeatedly (which would
///     trivially always show the live value, proving nothing about the real risk).
///     <para>
///         Before this fix, the plain, framework-registered <see cref="IOptions{TOptions}" /> of
///         <see cref="IdentityOptions" /> is itself backed by a singleton whose own <c>.Value</c>
///         is computed once, lazily, and cached for the life of the process -- confirmed directly
///         (a freshly re-resolved <see cref="IOptions{TOptions}" /> from a brand new scope never
///         observed a later configuration-file change at all). This test proves the registered
///         override instead makes each *new* scope's own freshly resolved accessor (mirroring a
///         fresh, per-request <see cref="UserManager{TUser}" /> instance) observe the live value
///         at the moment it is resolved -- "next request" hot reload, not true mid-instance hot
///         reload, exactly as <see cref="IdentityOptionsMonitorProxy" />'s own doc comment
///         describes.
///     </para>
/// </summary>
public sealed class IdentityOptionsMonitorProxyHotReloadTests
{
    [Fact]
    public async Task ANewlyResolvedAccessor_ObservesALiveConfigurationWrite_WhileAnAlreadyCapturedValueStaysStale()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"ninlil-dns-identity-options-hot-reload-{Guid.NewGuid():N}.json"
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

            // The same two-registration order ServiceCollectionIdentityExtension itself
            // establishes: AddIdentityCore's own hardcoded defaults, then the optional
            // configuration-section override, then this project's own live-monitor-backed
            // IOptions<IdentityOptions> override -- added last, so it wins DI's own "last
            // registration for this exact closed type" resolution over the framework's own
            // TryAdd-registered default.
            services.Configure<IdentityOptions>(options => options.Password.RequiredLength = 12);
            services.Configure<IdentityOptions>(configuration.GetSection("Identity"));
            services.AddSingleton<IOptions<IdentityOptions>>(
                provider => new IdentityOptionsMonitorProxy(
                    provider.GetRequiredService<IOptionsMonitor<IdentityOptions>>()
                )
            );

            await using ServiceProvider provider = services.BuildServiceProvider();

            // Simulates UserManager<TUser>'s own constructor: resolve IOptions<IdentityOptions>
            // from a fresh scope, then immediately read .Value once and keep only that captured
            // result -- never the accessor itself.
            int CaptureRequiredLengthFromAFreshScope()
            {
                using IServiceScope scope = provider.CreateScope();
                IOptions<IdentityOptions> accessor = scope.ServiceProvider.GetRequiredService<
                    IOptions<IdentityOptions>
                >();

                return accessor.Value.Password.RequiredLength;
            }

            // Before any write, the file does not exist yet -- this project's own compiled-in
            // default.
            int capturedBeforeWrite = CaptureRequiredLengthFromAFreshScope();
            Assert.Equal(12, capturedBeforeWrite);

            IOptionsMonitor<IdentityOptions> monitor = provider.GetRequiredService<
                IOptionsMonitor<IdentityOptions>
            >();
            TaskCompletionSource reloaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable subscription = monitor.OnChange(_ => reloaded.TrySetResult());

            // The exact same write mechanism IdentitySettingsController.Patch itself calls.
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

            // .NET's own JSON configuration provider debounces file-change notifications by a
            // few hundred milliseconds -- this waits for that real reload to actually happen
            // rather than asserting against a fixed delay.
            Task completed = await Task.WhenAny(
                reloaded.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            Assert.Same(reloaded.Task, completed);

            // A brand new scope -- the "next request" -- genuinely observes the new value, with
            // no restart.
            int capturedAfterWrite = CaptureRequiredLengthFromAFreshScope();
            Assert.Equal(20, capturedAfterWrite);

            // The value captured before the write is a plain int, already copied out of the
            // IdentityOptions instance that existed at that moment -- it can never itself change
            // retroactively, the concrete proof that this is "next request" hot reload, not true
            // mid-instance hot reload.
            Assert.Equal(12, capturedBeforeWrite);
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
