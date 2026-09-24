using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientStaticAddressMappings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="DnsClientStaticAddressMappingWriteRequest" />, referenced by
///     <see cref="Controllers.DnsClientStaticAddressMappingsController.Create" /> and
///     <see cref="Controllers.DnsClientStaticAddressMappingsController.Replace" />.
/// </summary>
public class DnsClientStaticAddressMappingWriteRequestExample
    : IExamplesProvider<DnsClientStaticAddressMappingWriteRequest>
{
    /// <summary>
    ///     This method returns a single, statically-addressed guest-VLAN host pinned to a MAC
    ///     address this server's own ARP table can never observe.
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
