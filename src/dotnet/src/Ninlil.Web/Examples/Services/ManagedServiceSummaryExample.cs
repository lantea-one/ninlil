using Ninlil.Dns.Server.Core.Services;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Services;

/// <summary>
///     This class supplies the Swagger example for <see cref="ManagedServiceSummary" />, as
///     returned by <see cref="Controllers.ServicesController.Start" /> and
///     <see cref="Controllers.ServicesController.Restart" />.
/// </summary>
public class ManagedServiceSummaryExample : IExamplesProvider<ManagedServiceSummary>
{
    /// <summary>
    ///     This method returns the plain DNS UDP listener, running.
    /// </summary>
    public ManagedServiceSummary GetExamples() =>
        new("plain-udp", ManagedServiceCategoryEnum.Transport, true);
}
