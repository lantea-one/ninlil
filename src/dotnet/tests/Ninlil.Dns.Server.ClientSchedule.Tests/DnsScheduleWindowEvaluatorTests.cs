using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.ClientSchedule;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientSchedule.Tests;

/// <summary>
///     This class tests <see cref="DnsScheduleWindowEvaluator.IsWithinBlockingWindow" />, the
///     pure window logic the engine builds on: same-day windows, day-of-week matching and windows
///     that wrap past midnight. Every test uses <c>"Asia/Tokyo"</c> (UTC+9, no daylight saving),
///     and <see cref="Constructor_SandboxIsNotAccidentallyTokyo" /> checks the host's local zone
///     is something else, so a pass cannot come from the evaluator using the local clock.
/// </summary>
public sealed class DnsScheduleWindowEvaluatorTests
{
    private static readonly TimeZoneInfo Tokyo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");

    /// <summary>
    ///     This test is a guard for the other tests rather than a test of the evaluator: it checks
    ///     that the host's local time zone is not Tokyo, which the class summary relies on.
    /// </summary>
    [Fact]
    public void Constructor_SandboxIsNotAccidentallyTokyo() =>
        Assert.NotEqual(Tokyo.Id, TimeZoneInfo.Local.Id);

    [Fact]
    public void IsWithinBlockingWindow_SameDayWindow_InsideWindow_ReturnsTrue()
    {
        // Monday 09:00-17:00 Tokyo time. 2026-09-14T01:00:00Z is Monday 10:00 in Tokyo (UTC+9),
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
        // 2026-09-14T09:00:00Z is Monday 18:00 in Tokyo, an hour after the 17:00 end.
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
        // 2026-09-15T01:00:00Z is Tuesday 10:00 in Tokyo: the time that blocks on Monday, but
        // Tuesday is not one of the schedule's days.
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
    ///     A plain <c>time &gt;= start &amp;&amp; time &lt;= end</c> check is wrong for a window
    ///     like 21:00-07:00, which runs past midnight into the next day. This test covers the
    ///     evening part (today is matched and the time is at or after the start).
    /// </summary>
    [Fact]
    public void IsWithinBlockingWindow_MidnightWrappingWindow_EveningPortion_ReturnsTrue()
    {
        // School nights: Sunday-Thursday, 21:00-07:00 Tokyo time.
        // 2026-09-14T13:00:00Z is Monday 22:00 in Tokyo: Monday is matched, and 22:00 is after
        // the 21:00 start.
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
    ///     This test covers the morning part of the same window (yesterday is matched and the time
    ///     is before the end), the half a same-day range check gets wrong.
    /// </summary>
    [Fact]
    public void IsWithinBlockingWindow_MidnightWrappingWindow_MorningPortion_ReturnsTrue()
    {
        // 2026-09-14T21:00:00Z is Tuesday 06:00 in Tokyo: Monday (yesterday) is matched, and
        // 06:00 is before the 07:00 end.
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
        // 2026-09-14T23:00:00Z is Tuesday 08:00 in Tokyo: after the previous night's 07:00 end
        // and before tonight's 21:00 start.
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
    ///     This test covers the case the
    ///     <see cref="DnsScheduleWindowEvaluator.IsWithinBlockingWindow" /> documentation names:
    ///     Friday morning is blocked (Thursday is a school night), Saturday morning is not (Friday
    ///     is not).
    /// </summary>
    [Fact]
    public void IsWithinBlockingWindow_MidnightWrappingWindow_FridayMorningBlocked_SaturdayMorningNot()
    {
        // 2026-09-17T21:00:00Z is Friday 06:00 in Tokyo; Thursday (yesterday) is a school night.
        bool fridayMorning = DnsScheduleWindowEvaluator.IsWithinBlockingWindow(
            SchoolNights(),
            new TimeOnly(21, 0),
            new TimeOnly(7, 0),
            Tokyo,
            DateTimeOffset.Parse("2026-09-17T21:00:00Z")
        );

        // 2026-09-18T21:00:00Z is Saturday 06:00 in Tokyo; Friday (yesterday) is not a school
        // night, so this is not blocked although the time of day matches.
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
    ///     This method returns "school nights" (Sunday through Thursday).
    /// </summary>
    private static DnsScheduleDayOfWeekEnum SchoolNights() =>
        DnsScheduleDayOfWeekEnum.Sunday
        | DnsScheduleDayOfWeekEnum.Monday
        | DnsScheduleDayOfWeekEnum.Tuesday
        | DnsScheduleDayOfWeekEnum.Wednesday
        | DnsScheduleDayOfWeekEnum.Thursday;
}
