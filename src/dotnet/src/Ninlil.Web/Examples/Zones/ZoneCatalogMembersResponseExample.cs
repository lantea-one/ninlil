using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Zones;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="ZoneCatalogMembersResponse" />, referenced by
///     <see cref="Controllers.ZonesController.ListCatalogMembers" />.
/// </summary>
public class ZoneCatalogMembersResponseExample : IExamplesProvider<ZoneCatalogMembersResponse>
{
    /// <inheritdoc />
    public ZoneCatalogMembersResponse GetExamples() =>
        new(["branch-office.example.com", "example.net"]);
}
