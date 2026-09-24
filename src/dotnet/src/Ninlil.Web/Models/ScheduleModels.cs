using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Controllers;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries the fields used to create or fully replace a time-of-day
///     parental-control schedule assigned directly to a known client
///     (<c>POST</c>/<c>PUT /api/clients/{clientId}/schedules</c>) -- mirrors
///     <see cref="ClientGroupScheduleWriteRequest" /> exactly, keyed by client instead of client
///     group. Unlike a pure filter-list join (<see cref="ClientListAssignmentWriteRequest" />),
///     a schedule has real, mutable fields of its own, so <see cref="ClientSchedulesController" />
///     supports <c>PUT</c> as well as <c>POST</c>/<c>DELETE</c>.
/// </summary>
/// <param name="DaysOfWeek">The subset of the week's days this schedule applies to -- never zero.</param>
/// <param name="StartTime">The local wall-clock time the blocking window begins, in <paramref name="TimeZoneId" />'s own time.</param>
/// <param name="EndTime">
///     The local wall-clock time the blocking window ends. Earlier than
///     <paramref name="StartTime" /> is a real, supported midnight-wrapping window (e.g. a
///     21:00-07:00 bedtime) -- only a value exactly equal to <paramref name="StartTime" /> is
///     rejected.
/// </param>
/// <param name="TimeZoneId">A real IANA time zone database identifier (e.g. <c>"America/New_York"</c>), validated at write time.</param>
/// <param name="IsDisabled">Whether the schedule starts out (or remains) disabled.</param>
/// <param name="Comments">Free-form operator notes about this schedule.</param>
public record ClientScheduleWriteRequest(
    DnsScheduleDayOfWeekEnum DaysOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string TimeZoneId,
    bool IsDisabled = false,
    string Comments = null
);

/// <summary>
///     This class carries a single client schedule's own fields, for list, single-item, and
///     creation responses.
/// </summary>
public record ClientScheduleDetail(
    Guid Id,
    Guid ClientId,
    DnsScheduleDayOfWeekEnum DaysOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string TimeZoneId,
    bool IsDisabled,
    string Comments,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc
);

/// <summary>
///     This class carries the fields used to create or fully replace a time-of-day
///     parental-control schedule assigned to a client group
///     (<c>POST</c>/<c>PUT /api/client-groups/{clientGroupId}/schedules</c>) -- see
///     <see cref="ClientScheduleWriteRequest" />'s own doc comment for the full field-by-field
///     reasoning, identical here.
/// </summary>
public record ClientGroupScheduleWriteRequest(
    DnsScheduleDayOfWeekEnum DaysOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string TimeZoneId,
    bool IsDisabled = false,
    string Comments = null
);

/// <summary>
///     This class carries a single client group schedule's own fields, for list, single-item,
///     and creation responses.
/// </summary>
public record ClientGroupScheduleDetail(
    Guid Id,
    Guid ClientGroupId,
    DnsScheduleDayOfWeekEnum DaysOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string TimeZoneId,
    bool IsDisabled,
    string Comments,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc
);
