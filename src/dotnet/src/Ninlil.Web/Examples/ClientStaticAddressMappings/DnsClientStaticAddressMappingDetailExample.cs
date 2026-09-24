using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientStaticAddressMappings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="DnsClientStaticAddressMappingDetail" />, referenced by
///     <see cref="Controllers.DnsClientStaticAddressMappingsController.Get" /> and
///     <see cref="Controllers.DnsClientStaticAddressMappingsController.Create" />.
/// </summary>
public class DnsClientStaticAddressMappingDetailExample
    : IExamplesProvider<DnsClientStaticAddressMappingDetail>
{
    /// <summary>
    ///     This method returns the same static address mapping
    ///     <see cref="DnsClientStaticAddressMappingWriteRequestExample" /> submits, as this server
    ///     would report it back -- a single, statically-addressed guest-VLAN host pinned to a MAC
    ///     address this server's own ARP table can never observe.
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
