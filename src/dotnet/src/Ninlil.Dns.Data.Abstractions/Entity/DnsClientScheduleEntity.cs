using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents a single time-of-day parental-control schedule assigned directly to
///     one <see cref="DnsClientEntity" /> -- while an active window is in effect, every DNS
///     resolution attempted by that client is blocked outright, exactly like a domain the
///     filtering engine's own block list already denies (see
///     <c>Ninlil.Dns.Server.Core.ClientSchedule.IDnsClientScheduleEngine</c> for the real
///     evaluation logic that consumes this table). This mirrors
///     <see cref="DnsClientGroupScheduleEntity" /> exactly, but keyed by <see cref="ClientId" />
///     instead of <c>ClientGroupId</c> -- a client's own direct schedule and its group's schedule
///     are independently checked, either one being currently active blocks the query (a union,
///     never one overriding the other), the same "separate entity per owner, not one table with
///     two nullable foreign keys" shape <c>DnsClientListAssignmentEntity</c>/
///     <c>DnsClientGroupListAssignmentEntity</c> already establish for filter-list assignment.
/// </summary>
public class DnsClientScheduleEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the identifier of the <see cref="DnsClientEntity" /> this
    ///     schedule belongs to.
    /// </summary>
    public Guid ClientId { get; set; }

    /// <summary>
    ///     This property contains the subset of the week's seven days this schedule applies to
    ///     (e.g. Sunday through Thursday for "school nights"). Never zero for a real, saved
    ///     schedule -- a schedule matching no day at all is a real user error, rejected at write
    ///     time by the owning controller.
    /// </summary>
    public DnsScheduleDayOfWeekEnum DaysOfWeek { get; set; }

    /// <summary>
    ///     This property contains the wall-clock time, in <see cref="TimeZoneId" />'s own local
    ///     time -- never UTC and never this server's own local time -- at which the blocking
    ///     window begins on each of <see cref="DaysOfWeek" />'s own matched days.
    /// </summary>
    public TimeOnly StartTime { get; set; }

    /// <summary>
    ///     This property contains the wall-clock time, in <see cref="TimeZoneId" />'s own local
    ///     time, at which the blocking window ends. <see cref="EndTime" /> earlier than
    ///     <see cref="StartTime" /> is a real, deliberately supported case -- a window that spans
    ///     midnight into the following calendar day (e.g. a 21:00-07:00 bedtime) -- never an
    ///     invalid value; see
    ///     <c>Ninlil.Dns.Server.Core.ClientSchedule.DnsScheduleWindowEvaluator.IsWithinBlockingWindow</c>
    ///     for the exact evaluation this shape needs. Only <see cref="EndTime" /> exactly equal to
    ///     <see cref="StartTime" /> (a zero-length window either way around the clock) is rejected
    ///     as meaningless, at write time.
    /// </summary>
    public TimeOnly EndTime { get; set; }

    /// <summary>
    ///     This property contains a real IANA time zone database identifier (e.g.
    ///     <c>"America/New_York"</c>) -- the time zone <see cref="StartTime" />/<see cref="EndTime" />
    ///     are meaningful wall-clock values in, resolved at evaluation time via
    ///     <see cref="TimeZoneInfo.FindSystemTimeZoneById" />. Validated against that same method
    ///     at write time and stored as the identifier string itself, deliberately never as a
    ///     numeric UTC offset -- a fixed offset breaks silently across a daylight-saving
    ///     transition, while a real zone id always resolves to whatever offset is actually in
    ///     effect on the day being evaluated.
    /// </summary>
    public string TimeZoneId { get; set; }

    /// <summary>
    ///     This property indicates whether this schedule is currently disabled. A disabled
    ///     schedule is kept on record but contributes nothing to
    ///     <c>IDnsClientScheduleEngine.EvaluateAsync</c>'s own blocking decision until re-enabled.
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    ///     This property contains free-form operator notes about this schedule.
    /// </summary>
    public string Comments { get; set; }
}
