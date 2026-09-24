using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientAddresses;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="ClientAddressDetail" />, referenced by
///     <see cref="Controllers.ClientAddressesController.Get" /> and
///     <see cref="Controllers.ClientAddressesController.Create" />.
/// </summary>
public class ClientAddressDetailExample : IExamplesProvider<ClientAddressDetail>
{
    /// <summary>
    ///     This method returns an IPv4 address belonging to the "Living Room TV" client
    ///     (<see cref="Clients.ClientDetailExample" />'s first example).
    /// </summary>
    public ClientAddressDetail GetExamples() =>
        new(
            Guid.Parse("f1a2b3c4-1234-4abc-9def-0123456789ab"),
            Guid.Parse("d4e5f6a1-8192-4ab1-c3d4-e5f6a1b2c3d3"),
            "b8:27:eb:12:34:56",
            InternetProtocolVersionEnum.IPv4,
            "192.0.2.42",
            DateTimeOffset.Parse("2026-09-10T09:00:00+00:00"),
            DateTimeOffset.Parse("2026-09-18T08:00:00+00:00"),
            DateTimeOffset.Parse("2026-09-10T09:00:00+00:00"),
            "b8:27:eb:12:34:56",
            "Raspberry Pi Foundation",
            false
        );
}
