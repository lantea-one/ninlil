using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Controllers;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries the fields used to create or fully replace a time-of-day
///     parental-control schedule for one known client (<c>POST</c> and <c>PUT</c> under
///     <c>/api/clients/{clientId}/schedules</c>). It is the client counterpart of
///     <see cref="ClientGroupScheduleWriteRequest" />. Unlike a list assignment
///     (<see cref="ClientListAssignmentWriteRequest" />), a schedule has fields of its own, so
///     <see cref="ClientSchedulesController" /> supports <c>PUT</c>.
/// </summary>
/// <param name="DaysOfWeek">The days of the week the schedule applies to; at least one is required.</param>
/// <param name="StartTime">The local time the blocking window begins, in <paramref name="TimeZoneId" />.</param>
/// <param name="EndTime">
///     The local time the blocking window ends. A time earlier than <paramref name="StartTime" />
///     makes a window that crosses midnight (e.g. a 21:00 to 07:00 bedtime); a time equal to it is
///     rejected.
/// </param>
/// <param name="TimeZoneId">An IANA time zone identifier (e.g. <c>"America/New_York"</c>), checked when saved.</param>
/// <param name="IsDisabled">Whether the schedule is disabled.</param>
/// <param name="Comments">Free-form operator notes about the schedule.</param>
public record ClientScheduleWriteRequest(
    DnsScheduleDayOfWeekEnum DaysOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string TimeZoneId,
    bool IsDisabled = false,
    string Comments = null
);

/// <summary>
///     This class carries a client schedule, as returned by the list, get and write endpoints
///     under <c>/api/clients/{clientId}/schedules</c>.
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
///     (<c>POST</c> and <c>PUT</c> under <c>/api/client-groups/{groupId}/schedules</c>). The
///     fields mean what they mean in <see cref="ClientScheduleWriteRequest" />.
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
///     This class carries a client group schedule, as returned by the list, get and write
///     endpoints under <c>/api/client-groups/{groupId}/schedules</c>.
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
