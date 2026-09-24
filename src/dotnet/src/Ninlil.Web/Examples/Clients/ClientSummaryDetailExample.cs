using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Clients;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="ClientSummaryDetail" />, referenced by
///     <see cref="Controllers.ClientsController.Summary" />.
/// </summary>
public class ClientSummaryDetailExample : IExamplesProvider<ClientSummaryDetail>
{
    /// <summary>
    ///     This method returns a small household network's counts: most devices online, two
    ///     registered since local midnight, evaluated against the default ten-minute window.
    /// </summary>
    public ClientSummaryDetail GetExamples() =>
        new(
            24,
            17,
            7,
            2,
            5,
            DateTimeOffset.Parse("2026-09-23T04:00:00+00:00"),
            TimeSpan.FromMinutes(10)
        );
}
