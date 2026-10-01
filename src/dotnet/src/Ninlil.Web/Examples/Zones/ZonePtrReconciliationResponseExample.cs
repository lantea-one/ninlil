using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Zones;

/// <summary>
///     This class supplies the Swagger example for <see cref="ZonePtrReconciliationResponse" />, as
///     returned by <see cref="Controllers.ZonesController.ReconcilePtr" />: two PTR records added
///     across two reverse zones, none removed, not skipped.
/// </summary>
public class ZonePtrReconciliationResponseExample : IExamplesProvider<ZonePtrReconciliationResponse>
{
    /// <inheritdoc />
    public ZonePtrReconciliationResponse GetExamples() => new(2, 0, 2, false, null);
}
