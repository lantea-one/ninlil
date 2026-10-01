using Ninlil.Dns.Server.Core.Services;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Services;

/// <summary>
///     This class supplies the Swagger example for <see cref="ManagedServiceSummary" />, as
///     returned by <see cref="Controllers.ServicesController.Stop" />.
/// </summary>
public class ManagedServiceSummaryStoppedExample : IExamplesProvider<ManagedServiceSummary>
{
    /// <summary>
    ///     This method returns the plain DNS UDP listener from
    ///     <see cref="ManagedServiceSummaryExample" />, now stopped.
    /// </summary>
    public ManagedServiceSummary GetExamples() =>
        new("plain-udp", ManagedServiceCategoryEnum.Transport, false);
}
