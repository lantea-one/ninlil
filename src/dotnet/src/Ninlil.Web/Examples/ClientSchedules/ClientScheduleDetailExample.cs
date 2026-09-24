using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientSchedules;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="ClientScheduleDetail" />, referenced by
///     <see cref="Controllers.ClientSchedulesController.Get" />/<see cref="Controllers.ClientSchedulesController.Create" />.
/// </summary>
public class ClientScheduleDetailExample : IExamplesProvider<ClientScheduleDetail>
{
    /// <summary>
    ///     This method returns the same schedule <see cref="ClientScheduleWriteRequestExample" />
    ///     submits, as this server would report it back, assigned to the "Living Room TV" client
    ///     (<see cref="Clients.ClientDetailExample" />'s first example).
    /// </summary>
    public ClientScheduleDetail GetExamples() =>
        new(
            Guid.Parse("c4d5e6f7-e6f7-4059-c5d4-e5f6a1b2c3e5"),
            Guid.Parse("d4e5f6a1-8192-4ab1-c3d4-e5f6a1b2c3d3"),
            DnsScheduleDayOfWeekEnum.Sunday
                | DnsScheduleDayOfWeekEnum.Monday
                | DnsScheduleDayOfWeekEnum.Tuesday
                | DnsScheduleDayOfWeekEnum.Wednesday
                | DnsScheduleDayOfWeekEnum.Thursday,
            new TimeOnly(21, 0),
            new TimeOnly(7, 0),
            "America/New_York",
            false,
            "School-night bedtime.",
            DateTimeOffset.Parse("2026-09-17T09:00:00+00:00"),
            null
        );
}
