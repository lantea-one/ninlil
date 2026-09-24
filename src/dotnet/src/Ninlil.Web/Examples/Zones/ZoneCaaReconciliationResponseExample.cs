using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Zones;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="ZoneCaaReconciliationResponse" />, referenced by
///     <see cref="Controllers.ZonesController.ReconcileCaa" />.
/// </summary>
public class ZoneCaaReconciliationResponseExample : IExamplesProvider<ZoneCaaReconciliationResponse>
{
    /// <inheritdoc />
    public ZoneCaaReconciliationResponse GetExamples() => new(1, 0, false, null);
}
