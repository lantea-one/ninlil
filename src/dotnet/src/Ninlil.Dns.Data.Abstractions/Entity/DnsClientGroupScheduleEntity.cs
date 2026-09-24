using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents a single time-of-day parental-control schedule assigned to one
///     <see cref="DnsClientGroupEntity" /> -- every client resolved into that group (via
///     <see cref="DnsClientEntity.ClientGroupId" />) is blocked outright for the schedule's own
///     active window, mirroring <see cref="DnsClientScheduleEntity" /> exactly but keyed by
///     <see cref="ClientGroupId" /> instead of a single client. See that class's own doc comment
///     for the full "separate entity per owner, independently checked, union not override"
///     reasoning this pair shares.
/// </summary>
public class DnsClientGroupScheduleEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the identifier of the <see cref="DnsClientGroupEntity" /> this
    ///     schedule belongs to.
    /// </summary>
    public Guid ClientGroupId { get; set; }

    /// <summary>
    ///     This property contains the subset of the week's seven days this schedule applies to --
    ///     see <see cref="DnsClientScheduleEntity.DaysOfWeek" />'s own doc comment.
    /// </summary>
    public DnsScheduleDayOfWeekEnum DaysOfWeek { get; set; }

    /// <summary>
    ///     This property contains the wall-clock time, in <see cref="TimeZoneId" />'s own local
    ///     time, at which the blocking window begins -- see
    ///     <see cref="DnsClientScheduleEntity.StartTime" />'s own doc comment.
    /// </summary>
    public TimeOnly StartTime { get; set; }

    /// <summary>
    ///     This property contains the wall-clock time, in <see cref="TimeZoneId" />'s own local
    ///     time, at which the blocking window ends -- see
    ///     <see cref="DnsClientScheduleEntity.EndTime" />'s own doc comment for the real,
    ///     deliberately supported midnight-wrapping case this field allows.
    /// </summary>
    public TimeOnly EndTime { get; set; }

    /// <summary>
    ///     This property contains a real IANA time zone database identifier -- see
    ///     <see cref="DnsClientScheduleEntity.TimeZoneId" />'s own doc comment.
    /// </summary>
    public string TimeZoneId { get; set; }

    /// <summary>
    ///     This property indicates whether this schedule is currently disabled -- see
    ///     <see cref="DnsClientScheduleEntity.IsDisabled" />'s own doc comment.
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    ///     This property contains free-form operator notes about this schedule.
    /// </summary>
    public string Comments { get; set; }
}
