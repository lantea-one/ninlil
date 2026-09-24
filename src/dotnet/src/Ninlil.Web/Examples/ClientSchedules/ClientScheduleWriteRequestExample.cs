using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientSchedules;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="ClientScheduleWriteRequest" />, referenced by
///     <see cref="Controllers.ClientSchedulesController.Create" />/<see cref="Controllers.ClientSchedulesController.Replace" />.
/// </summary>
public class ClientScheduleWriteRequestExample : IExamplesProvider<ClientScheduleWriteRequest>
{
    /// <summary>
    ///     This method returns a real, midnight-wrapping "school night" bedtime for a known
    ///     client -- Sunday through Thursday, 21:00 to 07:00 local time in
    ///     <c>"America/New_York"</c>, the classic case
    ///     <c>Ninlil.Dns.Server.Core.ClientSchedule.DnsScheduleWindowEvaluator</c>'s own doc
    ///     comment names directly.
    /// </summary>
    public ClientScheduleWriteRequest GetExamples() =>
        new(
            DnsScheduleDayOfWeekEnum.Sunday
                | DnsScheduleDayOfWeekEnum.Monday
                | DnsScheduleDayOfWeekEnum.Tuesday
                | DnsScheduleDayOfWeekEnum.Wednesday
                | DnsScheduleDayOfWeekEnum.Thursday,
            new TimeOnly(21, 0),
            new TimeOnly(7, 0),
            "America/New_York",
            false,
            "School-night bedtime."
        );
}
