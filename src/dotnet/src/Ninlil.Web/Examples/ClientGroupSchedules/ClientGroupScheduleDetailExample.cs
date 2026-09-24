using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientGroupSchedules;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="ClientGroupScheduleDetail" />, referenced by
///     <see cref="Controllers.ClientGroupSchedulesController.Get" />/<see cref="Controllers.ClientGroupSchedulesController.Create" />.
/// </summary>
public class ClientGroupScheduleDetailExample : IExamplesProvider<ClientGroupScheduleDetail>
{
    /// <summary>
    ///     This method returns the same schedule
    ///     <see cref="ClientGroupScheduleWriteRequestExample" /> submits, as this server would
    ///     report it back, assigned to the "Trusted LAN" group.
    /// </summary>
    public ClientGroupScheduleDetail GetExamples() =>
        new(
            Guid.Parse("e6f7a8b9-a8b9-427b-e7f6-a1b2c3d4e5f7"),
            Guid.Parse("c3d4e5f6-7081-49a0-b2c3-d4e5f6a1b2c2"),
            DnsScheduleDayOfWeekEnum.All,
            new TimeOnly(22, 0),
            new TimeOnly(6, 30),
            "Europe/London",
            false,
            null,
            DateTimeOffset.Parse("2026-09-17T09:10:00+00:00"),
            null
        );
}
