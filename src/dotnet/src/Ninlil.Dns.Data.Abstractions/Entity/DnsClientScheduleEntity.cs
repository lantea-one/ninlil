using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents a time-of-day parental-control schedule assigned to one
///     <see cref="DnsClientEntity" />. During an active window, every query from the client is
///     blocked, as if a block list denied it. <c>Ninlil.Dns.Server.Core.ClientSchedule.IDnsClientScheduleEngine</c>
///     evaluates it.
///     <para>
///         It mirrors <see cref="DnsClientGroupScheduleEntity" />, keyed by <see cref="ClientId" />
///         instead of <c>ClientGroupId</c>. The client's schedules and its group's schedules are
///         checked separately and any active one blocks the query; neither overrides the other.
///         Two tables are used rather than one with two nullable keys, as with
///         <c>DnsClientListAssignmentEntity</c> and <c>DnsClientGroupListAssignmentEntity</c>.
///     </para>
/// </summary>
public class DnsClientScheduleEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the identifier of the <see cref="DnsClientEntity" /> this
    ///     schedule belongs to.
    /// </summary>
    public Guid ClientId { get; set; }

    /// <summary>
    ///     This property contains the days of the week this schedule applies to (for example Sunday
    ///     through Thursday for "school nights"). It is never empty for a saved schedule; the
    ///     controller rejects a schedule with no days.
    /// </summary>
    public DnsScheduleDayOfWeekEnum DaysOfWeek { get; set; }

    /// <summary>
    ///     This property contains the local time in <see cref="TimeZoneId" /> (not UTC, and not the
    ///     server's local time) at which the blocking window begins on each day in
    ///     <see cref="DaysOfWeek" />.
    /// </summary>
    public TimeOnly StartTime { get; set; }

    /// <summary>
    ///     This property contains the local time in <see cref="TimeZoneId" /> at which the blocking
    ///     window ends. An <see cref="EndTime" /> earlier than <see cref="StartTime" /> is valid: the
    ///     window runs past midnight into the next day (for example a 21:00-07:00 bedtime); see
    ///     <c>Ninlil.Dns.Server.Core.ClientSchedule.DnsScheduleWindowEvaluator.IsWithinBlockingWindow</c>.
    ///     Only an <see cref="EndTime" /> equal to <see cref="StartTime" /> is rejected when saving.
    /// </summary>
    public TimeOnly EndTime { get; set; }

    /// <summary>
    ///     This property contains an IANA time zone identifier (for example
    ///     <c>"America/New_York"</c>): the zone <see cref="StartTime" /> and <see cref="EndTime" /> are
    ///     in. It is checked with <see cref="TimeZoneInfo.FindSystemTimeZoneById" /> when saved and
    ///     resolved the same way when evaluated. It is stored as the identifier, not a UTC offset,
    ///     because a fixed offset goes wrong across daylight-saving changes.
    /// </summary>
    public string TimeZoneId { get; set; }

    /// <summary>
    ///     This property indicates whether this schedule is disabled. A disabled schedule is kept
    ///     but ignored by <c>IDnsClientScheduleEngine.EvaluateAsync</c> until it is re-enabled.
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    ///     This property contains free-form operator notes about this schedule.
    /// </summary>
    public string Comments { get; set; }
}
