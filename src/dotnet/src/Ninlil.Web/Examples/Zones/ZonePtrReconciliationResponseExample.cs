using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Zones;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="ZonePtrReconciliationResponse" />, referenced by
///     <see cref="Controllers.ZonesController.ReconcilePtr" />.
/// </summary>
public class ZonePtrReconciliationResponseExample : IExamplesProvider<ZonePtrReconciliationResponse>
{
    /// <inheritdoc />
    public ZonePtrReconciliationResponse GetExamples() => new(2, 0, 2, false, null);
}
