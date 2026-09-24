using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientSchedules;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for a page of
///     <see cref="ClientScheduleDetail" />, referenced by
///     <see cref="Controllers.ClientSchedulesController.List" />.
/// </summary>
public class ClientScheduleDetailPagedResultExample
    : IExamplesProvider<PagedResult<ClientScheduleDetail>>
{
    /// <summary>
    ///     This method returns one page of two schedules for the "Living Room TV" client -- the
    ///     school-night bedtime <see cref="ClientScheduleDetailExample" /> already shows, plus a
    ///     currently-disabled weekend-morning schedule, demonstrating that a client may hold more
    ///     than one schedule at once and that a disabled one is still listed.
    /// </summary>
    public PagedResult<ClientScheduleDetail> GetExamples() =>
        new(
            [
                new ClientScheduleDetail(
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
                ),
                new ClientScheduleDetail(
                    Guid.Parse("d5e6f7a8-f7a8-416a-d6e5-f6a1b2c3d4f6"),
                    Guid.Parse("d4e5f6a1-8192-4ab1-c3d4-e5f6a1b2c3d3"),
                    DnsScheduleDayOfWeekEnum.Saturday | DnsScheduleDayOfWeekEnum.Sunday,
                    new TimeOnly(8, 0),
                    new TimeOnly(9, 0),
                    "America/New_York",
                    true,
                    "Weekend morning quiet time (currently disabled).",
                    DateTimeOffset.Parse("2026-09-17T09:05:00+00:00"),
                    null
                ),
            ],
            0,
            50,
            2
        );
}
