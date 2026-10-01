using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Zones;

/// <summary>
///     This class supplies the Swagger example for <see cref="ZoneCaaReconciliationResponse" />, as
///     returned by <see cref="Controllers.ZonesController.ReconcileCaa" />: one CAA record added,
///     none removed, not skipped.
/// </summary>
public class ZoneCaaReconciliationResponseExample : IExamplesProvider<ZoneCaaReconciliationResponse>
{
    /// <inheritdoc />
    public ZoneCaaReconciliationResponse GetExamples() => new(1, 0, false, null);
}
