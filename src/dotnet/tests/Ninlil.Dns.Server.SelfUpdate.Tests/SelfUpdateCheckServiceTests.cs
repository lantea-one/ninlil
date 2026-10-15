using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Server.SelfUpdate.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.SelfUpdate.Tests;

/// <summary>
///     This class tests <see cref="SelfUpdateCheckService.CheckNowAsync" />, which both the
///     periodic loop and the manual check use, against a fake HTTP handler. It covers an
///     unconfigured feed, newer, older and equal versions, malformed feeds, and a failed check
///     that must keep the last good result.
/// </summary>
public class SelfUpdateCheckServiceTests
{
    /// <summary>
    ///     This method builds a <see cref="SelfUpdateCheckService" /> with a fake HTTP handler and
    ///     a fixed running version.
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

        // A second service shares the cache, and its feed returns HTTP 500.
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

        // The last good result stays visible; only the error and the check time change.
        Assert.Equal(new Version(3, 0, 0), status.LatestKnownVersion);
        Assert.True(status.UpdateAvailable);
        Assert.Equal("https://example.com/3.0.0", status.ReleaseUrl);
        Assert.NotNull(status.LastCheckError);
    }

    /// <summary>
    ///     This method builds a status whose last check happened at the given time.
    /// </summary>
    private static SelfUpdateStatus StatusCheckedAt(DateTimeOffset? checkedAt, string error) =>
        SelfUpdateStatus.Empty(new Version(1, 0, 0)) with
        {
            Configured = true,
            LastCheckedAtUtc = checkedAt,
            LastCheckError = error,
        };

    [Fact]
    public void IsCheckDue_WhenNeverChecked_IsDue()
    {
        Assert.True(
            SelfUpdateCheckService.IsCheckDue(
                StatusCheckedAt(null, null),
                TimeSpan.FromHours(24),
                DateTimeOffset.UtcNow
            )
        );
    }

    [Theory]
    [InlineData(16, "boom", 24, true)] // a failure is retried at the next 15-minute tick
    [InlineData(5, "boom", 24, false)] // but not before it
    [InlineData(16, null, 24, false)] // a success waits the full interval
    [InlineData(25 * 60, null, 24, true)] // and is due once the interval has passed
    [InlineData(30, "boom", 1, true)] // a failure never waits longer than the tick
    public void IsCheckDue_AfterACheck_FollowsTheOutcome(
        int minutesAgo,
        string error,
        int intervalHours,
        bool expected
    )
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        SelfUpdateStatus status = StatusCheckedAt(now.AddMinutes(-minutesAgo), error);

        Assert.Equal(
            expected,
            SelfUpdateCheckService.IsCheckDue(status, TimeSpan.FromHours(intervalHours), now)
        );
    }

    [Fact]
    public async Task RunOneCheckIfDueAsync_AfterAFailedStartupCheck_RetriesAtTheNextTick()
    {
        // The feed fails on the first request and answers on every later one.
        int requests = 0;
        FakeHttpMessageHandler handler = new(_ =>
            ++requests == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"version":"2.0.0","releaseUrl":"https://example.com/2.0.0","notes":"n"}""",
                        System.Text.Encoding.UTF8,
                        "application/json"
                    ),
                }
        );
        (SelfUpdateCheckService service, _, SelfUpdateStatusCache cache) = Build(
            new SelfUpdateOptions
            {
                ReleaseFeedUrl = "https://example.com/feed.json",
                EnableAutomaticCheck = true,
                CheckInterval = TimeSpan.FromHours(24),
            },
            new Version(1, 0, 0),
            handler
        );

        // The startup check fails.
        await service.RunOneCheckIfDueAsync(true, DateTimeOffset.UtcNow, default);
        Assert.NotNull(cache.Current.LastCheckError);

        // Five minutes later nothing runs; one tick later the check runs and recovers.
        DateTimeOffset checkedAt = cache.Current.LastCheckedAtUtc.Value;
        await service.RunOneCheckIfDueAsync(false, checkedAt.AddMinutes(5), default);
        Assert.Equal(1, handler.RequestCount);

        await service.RunOneCheckIfDueAsync(false, checkedAt.AddMinutes(16), default);
        Assert.Equal(2, handler.RequestCount);
        Assert.Null(cache.Current.LastCheckError);
        Assert.Equal(new Version(2, 0, 0), cache.Current.LatestKnownVersion);

        // A success now waits the full interval again.
        DateTimeOffset succeededAt = cache.Current.LastCheckedAtUtc.Value;
        await service.RunOneCheckIfDueAsync(false, succeededAt.AddMinutes(16), default);
        Assert.Equal(2, handler.RequestCount);
    }
}
