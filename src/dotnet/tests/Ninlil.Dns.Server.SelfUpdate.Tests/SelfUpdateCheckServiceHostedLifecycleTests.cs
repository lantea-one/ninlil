using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Server.SelfUpdate.Tests.Fakes;

namespace Ninlil.Dns.Server.SelfUpdate.Tests;

/// <summary>
///     This class verifies <see cref="SelfUpdateCheckService" />'s own <see cref="BackgroundService" />
///     lifecycle -- specifically, that starting it runs a real check immediately, rather than only
///     after its own (deliberately much longer) periodic tick interval first elapses. See that
///     class's own doc comment for why an immediate first check matters for an operator who just
///     restarted onto a new build.
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

        // Poll briefly rather than a single fixed sleep -- the background loop's own first check
        // runs asynchronously as soon as StartAsync returns, not synchronously within it.
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
