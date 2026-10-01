using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.TransportSettings;

/// <summary>
///     This class supplies the Swagger example for <see cref="PlainTransportSettingsDetail" />, as
///     returned by <see cref="Controllers.TransportSettingsController.GetPlain" />.
/// </summary>
public class PlainTransportSettingsDetailExample : IExamplesProvider<PlainTransportSettingsDetail>
{
    /// <summary>
    ///     This method returns the defaults: port 53 on every address, a 30-second TCP idle timeout
    ///     and 512 TCP connections.
    /// </summary>
    public PlainTransportSettingsDetail GetExamples() =>
        new(
            Port: 53,
            ListenAddresses: [],
            TcpIdleTimeout: TimeSpan.FromSeconds(30),
            MaxConcurrentTcpConnections: 512
        );
}
