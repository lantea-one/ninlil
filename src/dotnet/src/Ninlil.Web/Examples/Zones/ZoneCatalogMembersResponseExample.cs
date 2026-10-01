using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Zones;

/// <summary>
///     This class supplies the Swagger example for <see cref="ZoneCatalogMembersResponse" />, as
///     returned by <see cref="Controllers.ZonesController.ListCatalogMembers" />: two member zone
///     names in alphabetical order.
/// </summary>
public class ZoneCatalogMembersResponseExample : IExamplesProvider<ZoneCatalogMembersResponse>
{
    /// <inheritdoc />
    public ZoneCatalogMembersResponse GetExamples() =>
        new(["branch-office.example.com", "example.net"]);
}
