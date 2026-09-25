using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Server.SelfUpdate.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.SelfUpdate.Tests;

/// <summary>
///     This class tests <see cref="SelfUpdateCheckService" /> as a <see cref="BackgroundService" />
///     with a fake HTTP handler: starting it runs a check at once instead of after the first
///     15-minute tick, and runs none when automatic checking is off.
/// </summary>
public class SelfUpdateCheckServiceHostedLifecycleTests
{
    [Fact]
    public async Task StartAsync_RunsARealCheckImmediately_WithoutWaitingForATick()
    {
        FakeHttpMessageHandler handler = FakeHttpMessageHandler.Returning(
            """{"version":"5.0.0","releaseUrl":"https://example.com/5.0.0"}"""
        );
        FakeCurrentAssemblyVersionProvider versionProvider = new(new Version(1, 0, 0));
        SelfUpdateStatusCache cache = new(versionProvider);
        SelfUpdateCheckService service = new(
            new SingleHandlerHttpClientFactory(handler),
            new StaticOptionsMonitor<SelfUpdateOptions>(
                new SelfUpdateOptions { ReleaseFeedUrl = "https://example.com/feed.json" }
            ),
            cache,
            versionProvider,
            NullLogger<SelfUpdateCheckService>.Instance
        );

        await service.StartAsync(CancellationToken.None);

        // Poll instead of sleeping once: the first check runs on the background loop after
        // StartAsync returns, not inside it.
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!cache.Current.Configured && DateTime.UtcNow < deadline)
            await Task.Delay(25);

        await service.StopAsync(CancellationToken.None);

        Assert.True(cache.Current.Configured);
        Assert.True(cache.Current.UpdateAvailable);
        Assert.Equal(new Version(5, 0, 0), cache.Current.LatestKnownVersion);
        Assert.True(handler.RequestCount >= 1);
    }

    [Fact]
    public async Task StartAsync_WhenCheckingIsDisabled_NeverMakesARequest()
    {
        FakeHttpMessageHandler handler = FakeHttpMessageHandler.Unreachable();
        FakeCurrentAssemblyVersionProvider versionProvider = new(new Version(1, 0, 0));
        SelfUpdateStatusCache cache = new(versionProvider);
        SelfUpdateCheckService service = new(
            new SingleHandlerHttpClientFactory(handler),
            new StaticOptionsMonitor<SelfUpdateOptions>(
                new SelfUpdateOptions
                {
                    EnableAutomaticCheck = false,
                    ReleaseFeedUrl = "https://example.com/feed.json",
                }
            ),
            cache,
            versionProvider,
            NullLogger<SelfUpdateCheckService>.Instance
        );

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(200);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(0, handler.RequestCount);
        Assert.False(cache.Current.Configured);
    }
}
