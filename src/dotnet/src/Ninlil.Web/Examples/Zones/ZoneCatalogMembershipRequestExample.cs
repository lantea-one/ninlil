using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Zones;

/// <summary>
///     This class supplies the Swagger example for <see cref="ZoneCatalogMembershipRequest" />, the
///     request body of <see cref="Controllers.ZonesController.JoinCatalog" />, naming a placeholder
///     catalog zone identifier.
/// </summary>
public class ZoneCatalogMembershipRequestExample : IExamplesProvider<ZoneCatalogMembershipRequest>
{
    /// <inheritdoc />
    public ZoneCatalogMembershipRequest GetExamples() =>
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
}
