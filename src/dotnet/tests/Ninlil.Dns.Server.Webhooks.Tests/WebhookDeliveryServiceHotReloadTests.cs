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
///     This class tests that a change to <see cref="WebhookDeliveryOptions.InitialRetryDelay" />
///     written with <see cref="Server.Presets.PresetConfigurationFileApplier" /> (as
///     <c>WebhookDeliverySettingsController.Patch</c> in <c>Ninlil.Web</c> does) changes the
///     backoff of the next attempt of an existing <see cref="WebhookDeliveryService" />, without
///     a restart. It uses a reload-on-change JSON configuration file, so the value flows through
///     <see cref="IOptionsMonitor{TOptions}" /> as in production, and a local Kestrel server
///     that always fails. The test reads the saved
///     <see cref="WebhookDeliveryAttemptEntity.NextRetryDueUtc" /> instead of timing a wait,
///     since only <see cref="WebhookDeliveryRetrySweepService" /> waits between attempts.
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
            // A reload-on-change JSON file provider, the same kind of source
            // WebApplication.CreateBuilder() sets up for appsettings.json.
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

            // Every attempt fails; the test only looks at the computed backoff.
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

            // The same write WebhookDeliverySettingsController.Patch makes, raising the initial
            // retry delay a hundredfold.
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

            // Poll for the new value rather than waiting for one OnChange: a late notification
            // from the first write can race this one, which made the test flaky.
            await WaitForReloadAsync(
                monitor,
                () => monitor.CurrentValue.InitialRetryDelay == TimeSpan.FromSeconds(1)
            );

            // The same service instance, built before the write, now computes the backoff from
            // the new delay, so the value is read per attempt, not captured at construction.
            // After attempt 2 the backoff is 2x InitialRetryDelay (see ComputeBackoffDelay).
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
                // Best-effort cleanup; a leftover temporary file is harmless.
            }
        }
    }

    /// <summary>
    ///     This method polls until <paramref name="condition" /> holds, for up to ten seconds,
    ///     because the JSON configuration provider reloads a changed file only after a short
    ///     debounce delay.
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
