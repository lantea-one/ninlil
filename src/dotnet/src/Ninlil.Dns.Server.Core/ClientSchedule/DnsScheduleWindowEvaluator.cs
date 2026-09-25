using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.ClientSchedule;

/// <summary>
///     This class answers the core question of every schedule check: given a schedule's days,
///     start, end and time zone, is its blocking window active at a given instant? It needs no
///     storage, so it lives here as a static helper that can be unit tested directly;
///     <c>Ninlil.Dns.Server.ClientSchedule.DnsClientScheduleEngine</c> calls it.
/// </summary>
public static class DnsScheduleWindowEvaluator
{
    /// <summary>
    ///     This method checks whether a schedule's blocking window is active at an instant.
    ///     <para>
    ///         <strong>Same-day window</strong> (<paramref name="startTime" /> at or before
    ///         <paramref name="endTime" />): active when today, in the schedule's time zone, is one
    ///         of <paramref name="daysOfWeek" /> and the local time is in <c>[startTime, endTime)</c>.
    ///         Equal start and end give an empty window.
    ///     </para>
    ///     <para>
    ///         <strong>Window across midnight</strong> (<paramref name="endTime" /> before
    ///         <paramref name="startTime" />, such as a 21:00 to 07:00 bedtime): active when (a)
    ///         today is a listed day and the time is at or after <paramref name="startTime" />, or
    ///         (b) yesterday was a listed day and the time is before <paramref name="endTime" />.
    ///         A Sunday to Thursday night schedule is therefore active on Friday morning (Thursday
    ///         is listed) but not on Saturday morning (Friday is not).
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
        // Use the schedule's time zone, not UTC or the server's zone. ConvertTime applies the
        // offset for this date, so daylight-saving changes need no special handling.
        DateTimeOffset localNow = TimeZoneInfo.ConvertTime(nowUtc, timeZone);
        DayOfWeek today = localNow.DayOfWeek;
        TimeOnly localTime = TimeOnly.FromDateTime(localNow.DateTime);

        if (startTime <= endTime)
        {
            // Same-day window: only today's day and time matter.
            return HasDay(daysOfWeek, today) && localTime >= startTime && localTime < endTime;
        }

        // Window across midnight; see the method summary.
        DayOfWeek yesterday = (DayOfWeek)(((int)today + 6) % 7);
        bool eveningPortion = HasDay(daysOfWeek, today) && localTime >= startTime;
        bool morningPortion = HasDay(daysOfWeek, yesterday) && localTime < endTime;

        return eveningPortion || morningPortion;
    }

    /// <summary>
    ///     This method checks whether a <see cref="DnsScheduleDayOfWeekEnum" /> value includes a
    ///     given <see cref="DayOfWeek" />.
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
