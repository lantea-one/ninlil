using Ninlil.Dns.Server.Core.Services;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Services;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="ManagedServiceSummary" />, referenced by
///     <see cref="Controllers.ServicesController.Stop" /> -- the same listener
///     <see cref="ManagedServiceSummaryExample" /> shows running, now genuinely stopped.
/// </summary>
public class ManagedServiceSummaryStoppedExample : IExamplesProvider<ManagedServiceSummary>
{
    /// <summary>
    ///     This method returns Plain DNS's own UDP listener, stopped.
    /// </summary>
    public ManagedServiceSummary GetExamples() =>
        new("plain-udp", ManagedServiceCategoryEnum.Transport, false);
}
