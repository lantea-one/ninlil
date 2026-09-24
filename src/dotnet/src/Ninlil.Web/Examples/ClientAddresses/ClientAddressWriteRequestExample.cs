using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientAddresses;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="ClientAddressWriteRequest" />, referenced by
///     <see cref="Controllers.ClientAddressesController.Create" />.
/// </summary>
public class ClientAddressWriteRequestExample : IExamplesProvider<ClientAddressWriteRequest>
{
    /// <summary>
    ///     This method returns a request linking a second, IPv6 identity onto an already-known
    ///     device -- confirming that its own IPv6 DUID belongs to the same real hardware as its
    ///     already-registered IPv4 MAC address.
    /// </summary>
    public ClientAddressWriteRequest GetExamples() =>
        new("00:03:00:01:aa:bb:cc:dd:ee:ff", InternetProtocolVersionEnum.IPv6);
}
