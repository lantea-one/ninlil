using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientStaticAddressMappings;

/// <summary>
///     This class supplies the Swagger example for
///     <see cref="DnsClientStaticAddressMappingWriteRequest" />, the request body of
///     <see cref="Controllers.DnsClientStaticAddressMappingsController.Create" /> and
///     <see cref="Controllers.DnsClientStaticAddressMappingsController.Replace" />.
/// </summary>
public class DnsClientStaticAddressMappingWriteRequestExample
    : IExamplesProvider<DnsClientStaticAddressMappingWriteRequest>
{
    /// <summary>
    ///     This method returns a statically addressed host on a guest VLAN, mapped to its MAC
    ///     address because the server sees neither a DHCP lease nor a neighbor-table entry for it.
    /// </summary>
    public DnsClientStaticAddressMappingWriteRequest GetExamples() =>
        new(
            "10.99.0.42/32",
            "aa:bb:cc:dd:ee:ff",
            InternetProtocolVersionEnum.IPv4,
            IsDisabled: false,
            Comments: "Guest VLAN printer, static address, no DHCP relay configured there."
        );
}
