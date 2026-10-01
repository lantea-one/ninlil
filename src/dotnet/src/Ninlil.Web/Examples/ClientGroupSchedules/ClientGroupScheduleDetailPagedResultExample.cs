using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientGroupSchedules;

/// <summary>
///     This class supplies the Swagger example for a page of
///     <see cref="ClientGroupScheduleDetail" />, as returned by
///     <see cref="Controllers.ClientGroupSchedulesController.List" />.
/// </summary>
public class ClientGroupScheduleDetailPagedResultExample
    : IExamplesProvider<PagedResult<ClientGroupScheduleDetail>>
{
    /// <summary>
    ///     This method returns a one-item page holding the nightly schedule from
    ///     <see cref="ClientGroupScheduleDetailExample" />.
    /// </summary>
    public PagedResult<ClientGroupScheduleDetail> GetExamples() =>
        new(
            [
                new ClientGroupScheduleDetail(
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
                ),
            ],
            0,
            50,
            1
        );
}
