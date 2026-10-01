using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.OutboundQuery;

/// <summary>
///     This class supplies the Swagger example for <see cref="OutboundQuerySettingsDetail" />, as
///     returned by <see cref="Controllers.OutboundQuerySettingsController.Get" />.
/// </summary>
public class OutboundQuerySettingsDetailExample : IExamplesProvider<OutboundQuerySettingsDetail>
{
    /// <summary>
    ///     This method returns the defaults: the socket pool on, 128 sockets per address family, no
    ///     excluded ports.
    /// </summary>
    public OutboundQuerySettingsDetail GetExamples() => new(true, 128, []);
}
