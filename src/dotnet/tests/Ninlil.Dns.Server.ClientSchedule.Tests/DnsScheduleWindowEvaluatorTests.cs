using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.ClientSchedule;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientSchedule.Tests;

/// <summary>
///     This class exhaustively proves <see cref="DnsScheduleWindowEvaluator.IsWithinBlockingWindow" />
///     -- the one piece of pure schedule-window logic every other layer (the engine, and
///     ultimately the pipeline) builds on. Every test in this class deliberately evaluates against
///     <c>"Asia/Tokyo"</c> (a fixed UTC+9 offset, no daylight-saving transitions), a time zone this
///     sandbox's own system time zone (<c>"America/New_York"</c>, confirmed via
///     <see cref="TimeZoneInfo.Local" /> in <see cref="Constructor_SandboxIsNotAccidentallyTokyo" />
///     below) is genuinely not -- so a passing assertion here can only be explained by real,
///     correct time-zone-aware conversion, never by the evaluator silently falling back to this
///     process's own local clock.
/// </summary>
public sealed class DnsScheduleWindowEvaluatorTests
{
    private static readonly TimeZoneInfo Tokyo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");

    /// <summary>
    ///     This test is a guard rail, not a real assertion about the evaluator itself: it proves
    ///     this sandbox's own local time zone is not already Tokyo, so every other test's own
    ///     "genuinely different from the sandbox's own zone" claim is actually true rather than an
    ///     accident of where these tests happen to run.
    /// </summary>
    [Fact]
    public void Constructor_SandboxIsNotAccidentallyTokyo() =>
        Assert.NotEqual(Tokyo.Id, TimeZoneInfo.Local.Id);

    [Fact]
    public void IsWithinBlockingWindow_SameDayWindow_InsideWindow_ReturnsTrue()
    {
        // Monday 09:00-17:00 Tokyo time. 2026-09-14T01:00:00Z is Monday 10:00 in Tokyo (UTC+9) --
        // inside the window.
        bool result = DnsScheduleWindowEvaluator.IsWithinBlockingWindow(
            DnsScheduleDayOfWeekEnum.Monday,
            new TimeOnly(9, 0),
            new TimeOnly(17, 0),
            Tokyo,
            DateTimeOffset.Parse("2026-09-14T01:00:00Z")
        );

        Assert.True(result);
    }

    [Fact]
    public void IsWithinBlockingWindow_SameDayWindow_AfterWindowEnds_ReturnsFalse()
    {
        // 2026-09-14T09:00:00Z is Monday 18:00 in Tokyo -- one hour past the window's own 17:00 end.
        bool result = DnsScheduleWindowEvaluator.IsWithinBlockingWindow(
            DnsScheduleDayOfWeekEnum.Monday,
            new TimeOnly(9, 0),
            new TimeOnly(17, 0),
            Tokyo,
            DateTimeOffset.Parse("2026-09-14T09:00:00Z")
        );

        Assert.False(result);
    }

    [Fact]
    public void IsWithinBlockingWindow_SameDayWindow_RightDayOfWeekNotMatched_ReturnsFalse()
    {
        // 2026-09-15T01:00:00Z is Tuesday 10:00 in Tokyo -- the same time-of-day the first test
        // proved blocking on Monday, but Tuesday is not one of this schedule's own matched days.
        bool result = DnsScheduleWindowEvaluator.IsWithinBlockingWindow(
            DnsScheduleDayOfWeekEnum.Monday,
            new TimeOnly(9, 0),
            new TimeOnly(17, 0),
            Tokyo,
            DateTimeOffset.Parse("2026-09-15T01:00:00Z")
        );

        Assert.False(result);
    }

    /// <summary>
    ///     This is the real midnight-wrap trap the task's own instructions call out by name: a
    ///     naive <c>time &gt;= start &amp;&amp; time &lt;= end</c> check is wrong for a window like
    ///     21:00-07:00, since the blocked period spans across midnight into the next calendar day.
    ///     This test proves the "evening portion" (today matched, at/after start) side of that
    ///     window.
    /// </summary>
    [Fact]
    public void IsWithinBlockingWindow_MidnightWrappingWindow_EveningPortion_ReturnsTrue()
    {
        // School nights: Sunday-Thursday, 21:00-07:00 Tokyo time.
        // 2026-09-14T13:00:00Z is Monday 22:00 in Tokyo -- Monday is matched, and 22:00 is at or
        // after the 21:00 start.
        bool result = DnsScheduleWindowEvaluator.IsWithinBlockingWindow(
            SchoolNights(),
            new TimeOnly(21, 0),
            new TimeOnly(7, 0),
            Tokyo,
            DateTimeOffset.Parse("2026-09-14T13:00:00Z")
        );

        Assert.True(result);
    }

    /// <summary>
    ///     This test proves the "morning portion" (yesterday matched, before end) side of the
    ///     same midnight-wrapping window -- the half a naive same-day range check gets wrong.
    /// </summary>
    [Fact]
    public void IsWithinBlockingWindow_MidnightWrappingWindow_MorningPortion_ReturnsTrue()
    {
        // 2026-09-14T21:00:00Z is Tuesday 06:00 in Tokyo -- Monday (yesterday, relative to
        // Tuesday) is matched, and 06:00 is before the 07:00 end.
        bool result = DnsScheduleWindowEvaluator.IsWithinBlockingWindow(
            SchoolNights(),
            new TimeOnly(21, 0),
            new TimeOnly(7, 0),
            Tokyo,
            DateTimeOffset.Parse("2026-09-14T21:00:00Z")
        );

        Assert.True(result);
    }

    [Fact]
    public void IsWithinBlockingWindow_MidnightWrappingWindow_MiddleOfTheDay_ReturnsFalse()
    {
        // 2026-09-14T23:00:00Z is Tuesday 08:00 in Tokyo -- past both the previous night's own
        // 07:00 end and nowhere near tonight's own 21:00 start.
        bool result = DnsScheduleWindowEvaluator.IsWithinBlockingWindow(
            SchoolNights(),
            new TimeOnly(21, 0),
            new TimeOnly(7, 0),
            Tokyo,
            DateTimeOffset.Parse("2026-09-14T23:00:00Z")
        );

        Assert.False(result);
    }

    /// <summary>
    ///     This test proves the exact distinction
    ///     <see cref="DnsScheduleWindowEvaluator.IsWithinBlockingWindow" />'s own doc comment
    ///     names directly: Friday morning is still blocked (Thursday, the previous day, is a
    ///     matched school night), but Saturday morning is not (Friday is not matched).
    /// </summary>
    [Fact]
    public void IsWithinBlockingWindow_MidnightWrappingWindow_FridayMorningBlocked_SaturdayMorningNot()
    {
        // 2026-09-17T21:00:00Z is Friday 06:00 in Tokyo -- Thursday (yesterday) is a school night.
        bool fridayMorning = DnsScheduleWindowEvaluator.IsWithinBlockingWindow(
            SchoolNights(),
            new TimeOnly(21, 0),
            new TimeOnly(7, 0),
            Tokyo,
            DateTimeOffset.Parse("2026-09-17T21:00:00Z")
        );

        // 2026-09-18T21:00:00Z is Saturday 06:00 in Tokyo -- Friday (yesterday) is not a school
        // night, so this must not be blocked even though the time-of-day matches exactly.
        bool saturdayMorning = DnsScheduleWindowEvaluator.IsWithinBlockingWindow(
            SchoolNights(),
            new TimeOnly(21, 0),
            new TimeOnly(7, 0),
            Tokyo,
            DateTimeOffset.Parse("2026-09-18T21:00:00Z")
        );

        Assert.True(fridayMorning);
        Assert.False(saturdayMorning);
    }

    /// <summary>
    ///     This method returns "school nights" (Sunday through Thursday) -- the exact example
    ///     this project's own task description names.
    /// </summary>
    private static DnsScheduleDayOfWeekEnum SchoolNights() =>
        DnsScheduleDayOfWeekEnum.Sunday
        | DnsScheduleDayOfWeekEnum.Monday
        | DnsScheduleDayOfWeekEnum.Tuesday
        | DnsScheduleDayOfWeekEnum.Wednesday
        | DnsScheduleDayOfWeekEnum.Thursday;
}
