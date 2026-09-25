using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents a time-of-day parental-control schedule of a
///     <see cref="DnsClientGroupEntity" />. Every client resolved into the group (through
///     <see cref="DnsClientEntity.ClientGroupId" />) is blocked during the schedule's window. It
///     mirrors <see cref="DnsClientScheduleEntity" />, keyed by <see cref="ClientGroupId" />
///     instead of a client; that class explains why the two are separate and how they combine.
/// </summary>
public class DnsClientGroupScheduleEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the identifier of the <see cref="DnsClientGroupEntity" /> this
    ///     schedule belongs to.
    /// </summary>
    public Guid ClientGroupId { get; set; }

    /// <summary>
    ///     This property contains the days of the week this schedule applies to; see
    ///     <see cref="DnsClientScheduleEntity.DaysOfWeek" />.
    /// </summary>
    public DnsScheduleDayOfWeekEnum DaysOfWeek { get; set; }

    /// <summary>
    ///     This property contains the local time in <see cref="TimeZoneId" /> at which the blocking
    ///     window begins; see <see cref="DnsClientScheduleEntity.StartTime" />.
    /// </summary>
    public TimeOnly StartTime { get; set; }

    /// <summary>
    ///     This property contains the local time in <see cref="TimeZoneId" /> at which the blocking
    ///     window ends; see <see cref="DnsClientScheduleEntity.EndTime" /> for windows that cross
    ///     midnight.
    /// </summary>
    public TimeOnly EndTime { get; set; }

    /// <summary>
    ///     This property contains an IANA time zone identifier; see
    ///     <see cref="DnsClientScheduleEntity.TimeZoneId" />.
    /// </summary>
    public string TimeZoneId { get; set; }

    /// <summary>
    ///     This property indicates whether this schedule is disabled; see
    ///     <see cref="DnsClientScheduleEntity.IsDisabled" />.
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    ///     This property contains free-form operator notes about this schedule.
    /// </summary>
    public string Comments { get; set; }
}
