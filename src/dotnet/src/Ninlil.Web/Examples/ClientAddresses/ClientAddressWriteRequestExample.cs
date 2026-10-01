using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientAddresses;

/// <summary>
///     This class supplies the Swagger example for <see cref="ClientAddressWriteRequest" />, the
///     request body of <see cref="Controllers.ClientAddressesController.Create" />.
/// </summary>
public class ClientAddressWriteRequestExample : IExamplesProvider<ClientAddressWriteRequest>
{
    /// <summary>
    ///     This method returns a request adding an IPv6 identity, a link-layer DUID (type 3)
    ///     embedding the device's MAC address, to a device already known by its IPv4 MAC address.
    /// </summary>
    public ClientAddressWriteRequest GetExamples() =>
        new("00:03:00:01:aa:bb:cc:dd:ee:ff", InternetProtocolVersionEnum.IPv6);
}
