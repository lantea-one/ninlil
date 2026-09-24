using Ninlil.Dns.Server.Core.Services;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Services;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="ManagedServiceSummary" />, referenced by
///     <see cref="Controllers.ServicesController.Start" />/
///     <see cref="Controllers.ServicesController.Restart" /> -- a real transport listener,
///     genuinely running after a successful start/restart.
/// </summary>
public class ManagedServiceSummaryExample : IExamplesProvider<ManagedServiceSummary>
{
    /// <summary>
    ///     This method returns Plain DNS's own UDP listener, running.
    /// </summary>
    public ManagedServiceSummary GetExamples() =>
        new("plain-udp", ManagedServiceCategoryEnum.Transport, true);
}
