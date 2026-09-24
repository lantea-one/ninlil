using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Webhooks.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Webhooks.Tests;

/// <summary>
///     This class proves, against a real, Kestrel-hosted fake HTTP server and a real
///     reload-on-change JSON configuration file -- not merely
///     <see cref="IOptionsMonitor{TOptions}.CurrentValue" /> read in isolation -- that a write
///     reaching <see cref="WebhookDeliveryOptions.InitialRetryDelay" /> via
///     <see cref="Server.Presets.PresetConfigurationFileApplier" /> (the exact mechanism
///     <c>Ninlil.Web.Controllers.WebhookDeliverySettingsController.Patch</c> itself calls)
///     changes the retry backoff computation the very next attempt -- whether the delivery's own
///     first attempt (<see cref="WebhookDeliveryService.DeliverAsync" />) or a later one resumed
///     by <see cref="WebhookDeliveryService.ResumeAttemptAsync" /> -- actually uses, with no
///     restart. Before this fix, <see cref="WebhookDeliveryService" /> read
///     <see cref="WebhookDeliveryOptions" /> via the usual, snapshotted
///     <see cref="IOptions{TOptions}" />, computed once and cached for the life of the process --
///     a write reaching the configuration file would never have been observed at all. This test
///     inspects the persisted <see cref="WebhookDeliveryAttemptEntity.NextRetryDueUtc" /> the
///     backoff computation actually produced, rather than timing a real wait -- fast and
///     deterministic, and a closer fit for this project's own current architecture, where
///     <see cref="WebhookDeliveryService" /> itself never waits out a backoff delay at all (see
///     that class's own doc comment): only <see cref="WebhookDeliveryRetrySweepService" />'s own
///     periodic sweep, driven by real wall-clock time, ever actually waits between two attempts.
/// </summary>
public sealed class WebhookDeliveryServiceHotReloadTests : IAsyncLifetime
{
    private WebApplication _fakeServer;
    private int _requestCount;
    private int _failuresBeforeSuccess;

    public async Task InitializeAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        _fakeServer = builder.Build();

        _fakeServer.MapPost(
            "/webhook",
            (HttpContext context) =>
            {
                int requestNumber = Interlocked.Increment(ref _requestCount);

                return requestNumber <= _failuresBeforeSuccess
                    ? Results.StatusCode(StatusCodes.Status500InternalServerError)
                    : Results.Ok();
            }
        );

        await _fakeServer.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _fakeServer.StopAsync();
        await _fakeServer.DisposeAsync();
    }

    private string TargetUrl => $"{_fakeServer.Urls.First()}/webhook";

    [Fact]
    public async Task AWriteThroughPresetConfigurationFileApplier_ChangesTheRetryDelayTheVeryNextAttemptComputes_WithNoRestart()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"ninlil-dns-webhook-initial-retry-delay-hot-reload-{Guid.NewGuid():N}.json"
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
            services.Configure<WebhookDeliveryOptions>(
                configuration.GetSection(WebhookDeliveryOptions.SectionName)
            );
            services.AddHttpClient(WebhookDeliveryService.HttpClientName);
            await using ServiceProvider provider = services.BuildServiceProvider();

            IOptionsMonitor<WebhookDeliveryOptions> monitor = provider.GetRequiredService<
                IOptionsMonitor<WebhookDeliveryOptions>
            >();

            // Seed a short initial retry delay and a generous max/timeout.
            await Server.Presets.PresetConfigurationFileApplier.ApplyAsync(
                [
                    new Server.Presets.PresetConfigurationSetting(
                        WebhookDeliveryOptions.SectionName,
                        nameof(WebhookDeliveryOptions.InitialRetryDelay),
                        "00:00:00.010"
                    ),
                    new Server.Presets.PresetConfigurationSetting(
                        WebhookDeliveryOptions.SectionName,
                        nameof(WebhookDeliveryOptions.MaxRetryDelay),
                        "00:01:00"
                    ),
                    new Server.Presets.PresetConfigurationSetting(
                        WebhookDeliveryOptions.SectionName,
                        nameof(WebhookDeliveryOptions.RequestTimeout),
                        "00:00:05"
                    ),
                ],
                path
            );

            await WaitForReloadAsync(
                monitor,
                () => monitor.CurrentValue.InitialRetryDelay == TimeSpan.FromMilliseconds(10)
            );

            FakeWebhookDeliveryAttemptRepository attemptRepository = new();
            WebhookSubscriptionEntity subscription = new()
            {
                Id = Guid.NewGuid(),
                Name = "hot-reload-subscription",
                TargetUrl = TargetUrl,
                EventTypeNames = ["DnsZoneChanged"],
                MaxDeliveryAttempts = 5,
            };

            WebhookDeliveryService deliveryService = new(
                new FakeWebhookSubscriptionRepository([subscription]),
                attemptRepository,
                new FakeUnitOfWork(),
                new WebhookCredentialProtector(new EphemeralDataProtectionProvider()),
                provider.GetRequiredService<IHttpClientFactory>(),
                monitor,
                NullLogger<WebhookDeliveryService>.Instance
            );

            // Every attempt fails -- this test only ever cares about the backoff the failure
            // computes, never a successful delivery.
            _failuresBeforeSuccess = int.MaxValue;

            await deliveryService.DeliverAsync(
                new DnsZoneChangedEvent(DateTimeOffset.UtcNow, Guid.NewGuid(), "example.com"),
                CancellationToken.None
            );

            WebhookDeliveryAttemptEntity attempt = Assert.Single(attemptRepository.Items);
            Assert.Equal(1, attempt.AttemptCount);

            TimeSpan firstDelay =
                attempt.NextRetryDueUtc!.Value - attempt.LastAttemptedAtUtc!.Value;
            Assert.True(
                firstDelay < TimeSpan.FromMilliseconds(200),
                $"Expected the short 10ms InitialRetryDelay to drive the first computed backoff, got {firstDelay}."
            );

            // The exact same write mechanism WebhookDeliverySettingsController.Patch itself
            // calls, this time raising the initial retry delay by two orders of magnitude.
            await Server.Presets.PresetConfigurationFileApplier.ApplyAsync(
                [
                    new Server.Presets.PresetConfigurationSetting(
                        WebhookDeliveryOptions.SectionName,
                        nameof(WebhookDeliveryOptions.InitialRetryDelay),
                        "00:00:01"
                    ),
                ],
                path
            );

            // Polls the actual settled value directly rather than trusting a single OnChange
            // firing to mean the reload is fully applied -- a second write to the same monitor
            // within one test can otherwise race a stray/delayed notification left over from the
            // first write against this one, observed directly as a real, reproducible flake.
            await WaitForReloadAsync(
                monitor,
                () => monitor.CurrentValue.InitialRetryDelay == TimeSpan.FromSeconds(1)
            );

            // Resuming the exact same persisted delivery -- the same WebhookDeliveryService
            // instance, constructed before the write -- now genuinely computes its next backoff
            // from the new, longer delay: proof the value was actually re-read on this later
            // attempt rather than captured once at construction (attempt 2's own multiplier is
            // 2x InitialRetryDelay, per WebhookDeliveryService.ComputeBackoffDelay).
            await deliveryService.ResumeAttemptAsync(attempt, CancellationToken.None);

            Assert.Equal(2, attempt.AttemptCount);
            TimeSpan secondDelay =
                attempt.NextRetryDueUtc!.Value - attempt.LastAttemptedAtUtc!.Value;
            Assert.True(
                secondDelay >= TimeSpan.FromMilliseconds(1900)
                    && secondDelay <= TimeSpan.FromMilliseconds(2500),
                $"Expected the new 1s InitialRetryDelay to drive a ~2s second backoff, got {secondDelay}."
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

    /// <summary>
    ///     This method waits for a real configuration reload to actually happen (.NET's own JSON
    ///     configuration provider debounces file-change notifications by a few hundred
    ///     milliseconds) rather than asserting against a fixed delay.
    /// </summary>
    private static async Task WaitForReloadAsync(
        IOptionsMonitor<WebhookDeliveryOptions> monitor,
        Func<bool> condition
    )
    {
        CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        while (!condition() && !cts.IsCancellationRequested)
            await Task.Delay(50);

        Assert.True(condition(), "The configuration reload did not happen within the timeout.");
    }
}
