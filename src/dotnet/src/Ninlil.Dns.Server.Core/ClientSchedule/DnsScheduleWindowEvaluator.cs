using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.ClientSchedule;

/// <summary>
///     This class holds the one piece of pure, storage-free logic every schedule check ultimately
///     reduces to: given a schedule's own days/start/end/time-zone and the current instant, is
///     the blocking window currently active? Kept here, in <c>Server.Core</c>, rather than inside
///     the engine implementation itself, since it needs no repository or identity-resolution
///     dependency at all -- a real, dedicated static helper, directly and exhaustively unit
///     testable without any dependency-injection setup, the same "extract the pure core, test it
///     directly" shape <c>Ninlil.Dns.Server.Filtering.DnsFilterEngine.BuildWildcardRegexPattern</c>
///     already establishes for its own bit of pure logic.
/// </summary>
public static class DnsScheduleWindowEvaluator
{
    /// <summary>
    ///     This method checks whether a schedule's own blocking window is active at a given
    ///     instant.
    ///     <para>
    ///         <strong>Same-day window</strong> (<paramref name="startTime" /> earlier than or
    ///         equal to <paramref name="endTime" />): active exactly when today, in the
    ///         schedule's own local time zone, is one of <paramref name="daysOfWeek" />'s own
    ///         matched days and the local time of day falls within <c>[startTime, endTime)</c>.
    ///     </para>
    ///     <para>
    ///         <strong>Midnight-wrapping window</strong> (<paramref name="endTime" /> earlier
    ///         than <paramref name="startTime" />, e.g. a 21:00-07:00 bedtime): a naive
    ///         "<c>time &gt;= start &amp;&amp; time &lt;= end</c>" check is wrong here, since the
    ///         blocked period spans across midnight into the next calendar day. Active in either
    ///         of two cases instead: (a) <em>today</em> is a matched day and the local time is at
    ///         or after <paramref name="startTime" /> -- the evening portion, running into
    ///         tonight; or (b) <em>yesterday</em> (relative to the instant being evaluated) was a
    ///         matched day and the local time is before <paramref name="endTime" /> -- the
    ///         early-morning portion, spilled over from the previous night. A schedule covering
    ///         Sunday-Thursday nights, for example, is therefore still active on Friday morning
    ///         before <paramref name="endTime" /> (Thursday, the previous day, is a matched day),
    ///         but not on Saturday morning (Friday, the previous day, is not).
    ///     </para>
    /// </summary>
    /// <param name="daysOfWeek">The subset of the week's days this schedule applies to.</param>
    /// <param name="startTime">The local wall-clock time the blocking window begins.</param>
    /// <param name="endTime">The local wall-clock time the blocking window ends.</param>
    /// <param name="timeZone">The time zone <paramref name="startTime" />/<paramref name="endTime" /> are meaningful local values in.</param>
    /// <param name="nowUtc">The instant to evaluate, in UTC.</param>
    /// <returns><see langword="true" /> if the blocking window is currently active.</returns>
    public static bool IsWithinBlockingWindow(
        DnsScheduleDayOfWeekEnum daysOfWeek,
        TimeOnly startTime,
        TimeOnly endTime,
        TimeZoneInfo timeZone,
        DateTimeOffset nowUtc
    )
    {
        // Convert the evaluated instant into the schedule's own local wall-clock time -- never
        // UTC and never this server's own local time, per DnsClientScheduleEntity.TimeZoneId's
        // own doc comment. TimeZoneInfo.ConvertTime resolves the correct offset for the exact
        // date being evaluated, so a daylight-saving transition is handled correctly with no
        // special-casing needed here.
        DateTimeOffset localNow = TimeZoneInfo.ConvertTime(nowUtc, timeZone);
        DayOfWeek today = localNow.DayOfWeek;
        TimeOnly localTime = TimeOnly.FromDateTime(localNow.DateTime);

        if (startTime <= endTime)
        {
            // Same-day window: the whole blocking period sits inside one calendar day, so only
            // today's own day-of-week and time of day matter.
            return HasDay(daysOfWeek, today) && localTime >= startTime && localTime < endTime;
        }

        // Midnight-wrapping window -- see this method's own doc comment for the full reasoning.
        DayOfWeek yesterday = (DayOfWeek)(((int)today + 6) % 7);
        bool eveningPortion = HasDay(daysOfWeek, today) && localTime >= startTime;
        bool morningPortion = HasDay(daysOfWeek, yesterday) && localTime < endTime;

        return eveningPortion || morningPortion;
    }

    /// <summary>
    ///     This method checks whether a <see cref="DnsScheduleDayOfWeekEnum" /> value includes a
    ///     given ordinary <see cref="DayOfWeek" />.
    /// </summary>
    private static bool HasDay(DnsScheduleDayOfWeekEnum daysOfWeek, DayOfWeek day)
    {
        DnsScheduleDayOfWeekEnum flag = day switch
        {
            DayOfWeek.Sunday => DnsScheduleDayOfWeekEnum.Sunday,
            DayOfWeek.Monday => DnsScheduleDayOfWeekEnum.Monday,
            DayOfWeek.Tuesday => DnsScheduleDayOfWeekEnum.Tuesday,
            DayOfWeek.Wednesday => DnsScheduleDayOfWeekEnum.Wednesday,
            DayOfWeek.Thursday => DnsScheduleDayOfWeekEnum.Thursday,
            DayOfWeek.Friday => DnsScheduleDayOfWeekEnum.Friday,
            DayOfWeek.Saturday => DnsScheduleDayOfWeekEnum.Saturday,
            _ => DnsScheduleDayOfWeekEnum.None,
        };

        return (daysOfWeek & flag) != 0;
    }
}
