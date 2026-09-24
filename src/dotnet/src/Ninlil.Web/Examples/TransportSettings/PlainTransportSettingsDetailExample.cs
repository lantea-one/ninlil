using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.TransportSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="PlainTransportSettingsDetail" />, referenced by
///     <see cref="Controllers.TransportSettingsController.GetPlain" />.
/// </summary>
public class PlainTransportSettingsDetailExample : IExamplesProvider<PlainTransportSettingsDetail>
{
    /// <summary>This method returns this class's own compiled-in defaults.</summary>
    public PlainTransportSettingsDetail GetExamples() =>
        new(Port: 53, TcpIdleTimeout: TimeSpan.FromSeconds(30), MaxConcurrentTcpConnections: 512);
}
