using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientStaticAddressMappings;

/// <summary>
///     This class supplies the Swagger example for
///     <see cref="DnsClientStaticAddressMappingDetail" />, as returned by
///     <see cref="Controllers.DnsClientStaticAddressMappingsController.Get" /> and
///     <see cref="Controllers.DnsClientStaticAddressMappingsController.Create" />.
/// </summary>
public class DnsClientStaticAddressMappingDetailExample
    : IExamplesProvider<DnsClientStaticAddressMappingDetail>
{
    /// <summary>
    ///     This method returns the mapping
    ///     <see cref="DnsClientStaticAddressMappingWriteRequestExample" /> submits, as the server
    ///     reports it.
    /// </summary>
    public DnsClientStaticAddressMappingDetail GetExamples() =>
        new(
            Guid.Parse("c3d4e5f6-3435-4637-a839-c3d4e5f6a7b3"),
            "10.99.0.42/32",
            "aa:bb:cc:dd:ee:ff",
            InternetProtocolVersionEnum.IPv4,
            false,
            "Guest VLAN printer, static address, no DHCP relay configured there.",
            DateTimeOffset.Parse("2026-09-18T09:00:00+00:00"),
            null
        );
}
