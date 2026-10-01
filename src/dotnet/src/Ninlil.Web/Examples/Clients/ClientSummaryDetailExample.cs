using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Clients;

/// <summary>
///     This class supplies the Swagger example for <see cref="ClientSummaryDetail" />, as returned
///     by <see cref="Controllers.ClientsController.Summary" />.
/// </summary>
public class ClientSummaryDetailExample : IExamplesProvider<ClientSummaryDetail>
{
    /// <summary>
    ///     This method returns the counts for a small household: 24 clients, 17 online and so 7
    ///     offline, 2 registered since a caller's local midnight (passed as <c>newSinceUtc</c>) and
    ///     5 favorites, measured against the default 10-minute online window.
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
