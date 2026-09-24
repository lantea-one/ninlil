using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.OutboundQuery;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="OutboundQuerySettingsDetail" />, referenced by
///     <see cref="Controllers.OutboundQuerySettingsController.Get" />.
/// </summary>
public class OutboundQuerySettingsDetailExample : IExamplesProvider<OutboundQuerySettingsDetail>
{
    /// <summary>
    ///     This method returns this feature's own compiled-in defaults: pooling on (a pure
    ///     hardening improvement with no new attack surface), 128 sockets per address family, and
    ///     no excluded ports.
    /// </summary>
    public OutboundQuerySettingsDetail GetExamples() => new(true, 128, []);
}
