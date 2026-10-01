using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientGroupSchedules;

/// <summary>
///     This class supplies the Swagger example for <see cref="ClientGroupScheduleWriteRequest" />,
///     the request body of <see cref="Controllers.ClientGroupSchedulesController.Create" /> and
///     <see cref="Controllers.ClientGroupSchedulesController.Replace" />.
/// </summary>
public class ClientGroupScheduleWriteRequestExample
    : IExamplesProvider<ClientGroupScheduleWriteRequest>
{
    /// <summary>
    ///     This method returns a bedtime for every client in the "Trusted LAN" group: every day,
    ///     22:00 to 06:30 local time in <c>"Europe/London"</c>. The end is earlier than the start,
    ///     so the window crosses midnight.
    /// </summary>
    public ClientGroupScheduleWriteRequest GetExamples() =>
        new(
            DnsScheduleDayOfWeekEnum.All,
            new TimeOnly(22, 0),
            new TimeOnly(6, 30),
            "Europe/London"
        );
}
