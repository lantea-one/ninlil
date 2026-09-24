using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientGroupSchedules;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="ClientGroupScheduleWriteRequest" />, referenced by
///     <see cref="Controllers.ClientGroupSchedulesController.Create" />/<see cref="Controllers.ClientGroupSchedulesController.Replace" />.
/// </summary>
public class ClientGroupScheduleWriteRequestExample
    : IExamplesProvider<ClientGroupScheduleWriteRequest>
{
    /// <summary>
    ///     This method returns a real, midnight-wrapping bedtime applied to every client in the
    ///     "Trusted LAN" group (<see cref="ClientGroups.ClientGroupDetailExample" />'s second
    ///     example) at once -- every day of the week, 22:00 to 06:30 local time in
    ///     <c>"Europe/London"</c>.
    /// </summary>
    public ClientGroupScheduleWriteRequest GetExamples() =>
        new(
            DnsScheduleDayOfWeekEnum.All,
            new TimeOnly(22, 0),
            new TimeOnly(6, 30),
            "Europe/London"
        );
}
