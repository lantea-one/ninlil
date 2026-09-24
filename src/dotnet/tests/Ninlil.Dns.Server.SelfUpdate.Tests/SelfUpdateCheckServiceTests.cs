using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Server.SelfUpdate.Tests.Fakes;

namespace Ninlil.Dns.Server.SelfUpdate.Tests;

/// <summary>
///     This class verifies <see cref="SelfUpdateCheckService.CheckNowAsync" /> -- the shared
///     logic behind both this service's own periodic loop and a manual "check now" REST action --
///     against a fake HTTP transport, covering an unconfigured feed, a real newer/older/equal
///     version comparison, and a failed check that must preserve the last known-good result.
/// </summary>
public class SelfUpdateCheckServiceTests
{
    /// <summary>
    ///     This method builds a real <see cref="SelfUpdateCheckService" /> wired to a fake
    ///     transport and a fixed "currently running" version, ready for a test to drive directly.
    /// </summary>
    private static (
        SelfUpdateCheckService Service,
        FakeHttpMessageHandler Handler,
        SelfUpdateStatusCache Cache
    ) Build(SelfUpdateOptions options, Version currentVersion, FakeHttpMessageHandler handler)
    {
        FakeCurrentAssemblyVersionProvider versionProvider = new(currentVersion);
        SelfUpdateStatusCache cache = new(versionProvider);
        SelfUpdateCheckService service = new(
            new SingleHandlerHttpClientFactory(handler),
            new StaticOptionsMonitor<SelfUpdateOptions>(options),
            cache,
            versionProvider,
            NullLogger<SelfUpdateCheckService>.Instance
        );

        return (service, handler, cache);
    }

    [Fact]
    public async Task CheckNowAsync_WithNoReleaseFeedUrl_NeverMakesARequest()
    {
        FakeHttpMessageHandler handler = FakeHttpMessageHandler.Unreachable();
        (SelfUpdateCheckService service, _, _) = Build(
            new SelfUpdateOptions { ReleaseFeedUrl = string.Empty },
            new Version(1, 0, 0, 0),
            handler
        );

        SelfUpdateStatus status = await service.CheckNowAsync();

        Assert.False(status.Configured);
        Assert.False(status.UpdateAvailable);
        Assert.Null(status.LatestKnownVersion);
        Assert.NotNull(status.LastCheckedAtUtc);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task CheckNowAsync_WithANewerFeedVersion_ReportsUpdateAvailable()
    {
        FakeHttpMessageHandler handler = FakeHttpMessageHandler.Returning(
            """{"version":"2.0.0","releaseUrl":"https://example.com/2.0.0","notes":"Real notes."}"""
        );
        (SelfUpdateCheckService service, _, _) = Build(
            new SelfUpdateOptions { ReleaseFeedUrl = "https://example.com/feed.json" },
            new Version(1, 0, 0),
            handler
        );

        SelfUpdateStatus status = await service.CheckNowAsync();

        Assert.True(status.Configured);
        Assert.True(status.UpdateAvailable);
        Assert.Equal(new Version(2, 0, 0), status.LatestKnownVersion);
        Assert.Equal("https://example.com/2.0.0", status.ReleaseUrl);
        Assert.Equal("Real notes.", status.ReleaseNotes);
        Assert.Null(status.LastCheckError);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task CheckNowAsync_WithAnOlderOrEqualFeedVersion_ReportsNoUpdateAvailable()
    {
        FakeHttpMessageHandler handler = FakeHttpMessageHandler.Returning(
            """{"version":"1.0.0","releaseUrl":"https://example.com/1.0.0"}"""
        );
        (SelfUpdateCheckService service, _, _) = Build(
            new SelfUpdateOptions { ReleaseFeedUrl = "https://example.com/feed.json" },
            new Version(1, 0, 0),
            handler
        );

        SelfUpdateStatus status = await service.CheckNowAsync();

        Assert.True(status.Configured);
        Assert.False(status.UpdateAvailable);
        Assert.Equal(new Version(1, 0, 0), status.LatestKnownVersion);
    }

    [Fact]
    public async Task CheckNowAsync_WithMalformedJson_RecordsAFailureWithoutThrowing()
    {
        FakeHttpMessageHandler handler = FakeHttpMessageHandler.Returning("not json at all");
        (SelfUpdateCheckService service, _, _) = Build(
            new SelfUpdateOptions { ReleaseFeedUrl = "https://example.com/feed.json" },
            new Version(1, 0, 0),
            handler
        );

        SelfUpdateStatus status = await service.CheckNowAsync();

        Assert.True(status.Configured);
        Assert.False(status.UpdateAvailable);
        Assert.NotNull(status.LastCheckError);
    }

    [Fact]
    public async Task CheckNowAsync_WithAnUnparseableVersion_RecordsAFailureWithoutThrowing()
    {
        FakeHttpMessageHandler handler = FakeHttpMessageHandler.Returning(
            """{"version":"not-a-version"}"""
        );
        (SelfUpdateCheckService service, _, _) = Build(
            new SelfUpdateOptions { ReleaseFeedUrl = "https://example.com/feed.json" },
            new Version(1, 0, 0),
            handler
        );

        SelfUpdateStatus status = await service.CheckNowAsync();

        Assert.NotNull(status.LastCheckError);
        Assert.False(status.UpdateAvailable);
    }

    [Fact]
    public async Task CheckNowAsync_WhenAFollowingCheckFails_PreservesTheLastKnownGoodResult()
    {
        FakeHttpMessageHandler successHandler = FakeHttpMessageHandler.Returning(
            """{"version":"3.0.0","releaseUrl":"https://example.com/3.0.0","notes":"Good."}"""
        );
        FakeCurrentAssemblyVersionProvider versionProvider = new(new Version(1, 0, 0));
        SelfUpdateStatusCache cache = new(versionProvider);
        SelfUpdateOptions options = new() { ReleaseFeedUrl = "https://example.com/feed.json" };

        SelfUpdateCheckService successService = new(
            new SingleHandlerHttpClientFactory(successHandler),
            new StaticOptionsMonitor<SelfUpdateOptions>(options),
            cache,
            versionProvider,
            NullLogger<SelfUpdateCheckService>.Instance
        );
        await successService.CheckNowAsync();

        // A second check, sharing the same cache, whose own transport now fails outright.
        FakeHttpMessageHandler failingHandler = FakeHttpMessageHandler.ReturningStatus(
            HttpStatusCode.InternalServerError
        );
        SelfUpdateCheckService failingService = new(
            new SingleHandlerHttpClientFactory(failingHandler),
            new StaticOptionsMonitor<SelfUpdateOptions>(options),
            cache,
            versionProvider,
            NullLogger<SelfUpdateCheckService>.Instance
        );
        SelfUpdateStatus status = await failingService.CheckNowAsync();

        // The last genuinely known-good result must still be visible through the transient
        // failure -- only the error and the checked-at timestamp change.
        Assert.Equal(new Version(3, 0, 0), status.LatestKnownVersion);
        Assert.True(status.UpdateAvailable);
        Assert.Equal("https://example.com/3.0.0", status.ReleaseUrl);
        Assert.NotNull(status.LastCheckError);
    }
}
