using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Presets;
using Ninlil.Dns.Server.QueryLog;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class proves, directly against the real .NET configuration/options binding pipeline
///     rather than through the full HTTP host, that <see cref="QueryLoggingOptions.PrivacyLevel" />
///     genuinely hot-reloads: a write <see cref="PresetConfigurationFileApplier" /> merges into a
///     JSON file is visible on <see cref="IOptionsMonitor{TOptions}" />'s own
///     <c>CurrentValue</c> with no restart, mirroring <see cref="AppearanceOptionsHotReloadTests" />'s
///     own identical shape and reasoning for <c>AppearanceOptions</c> -- the same real, reload-
///     on-change configuration source, the same real merge-patch write mechanism, just against a
///     different options class's own one hot-reloading field.
/// </summary>
public class QueryLoggingOptionsHotReloadTests
{
    [Fact]
    public async Task AWriteThroughPresetConfigurationFileApplier_IsVisibleOnIOptionsMonitorWithNoRestart()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"ninlil-dns-querylogging-hot-reload-{Guid.NewGuid():N}.json"
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
            services.Configure<QueryLoggingOptions>(
                configuration.GetSection(QueryLoggingOptions.SectionName)
            );
            await using ServiceProvider provider = services.BuildServiceProvider();

            IOptionsMonitor<QueryLoggingOptions> monitor = provider.GetRequiredService<
                IOptionsMonitor<QueryLoggingOptions>
            >();

            // Before any write, the file does not exist yet -- this class's own compiled-in
            // default, exactly as a fresh installation would see it: full detail, unchanged from
            // before this privacy scale even existed.
            Assert.Equal(QueryLogPrivacyLevelEnum.Full, monitor.CurrentValue.PrivacyLevel);

            TaskCompletionSource reloaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable subscription = monitor.OnChange(_ => reloaded.TrySetResult());

            await PresetConfigurationFileApplier.ApplyAsync(
                [
                    new PresetConfigurationSetting(
                        QueryLoggingOptions.SectionName,
                        nameof(QueryLoggingOptions.PrivacyLevel),
                        QueryLogPrivacyLevelEnum.FullyAnonymous.ToString()
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

            Assert.Equal(
                QueryLogPrivacyLevelEnum.FullyAnonymous,
                monitor.CurrentValue.PrivacyLevel
            );
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
