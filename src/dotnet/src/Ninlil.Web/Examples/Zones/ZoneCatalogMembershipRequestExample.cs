using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Zones;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="ZoneCatalogMembershipRequest" />, referenced by
///     <see cref="Controllers.ZonesController.JoinCatalog" />.
/// </summary>
public class ZoneCatalogMembershipRequestExample : IExamplesProvider<ZoneCatalogMembershipRequest>
{
    /// <inheritdoc />
    public ZoneCatalogMembershipRequest GetExamples() =>
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
}
