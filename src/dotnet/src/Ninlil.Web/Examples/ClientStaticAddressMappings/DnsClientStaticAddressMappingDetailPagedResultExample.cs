using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientStaticAddressMappings;

/// <summary>
///     This class supplies the Swagger example for a page of
///     <see cref="DnsClientStaticAddressMappingDetail" />, as returned by
///     <see cref="Controllers.DnsClientStaticAddressMappingsController.List" />.
/// </summary>
public class DnsClientStaticAddressMappingDetailPagedResultExample
    : IExamplesProvider<PagedResult<DnsClientStaticAddressMappingDetail>>
{
    /// <summary>
    ///     This method returns a one-item page holding the mapping from
    ///     <see cref="DnsClientStaticAddressMappingDetailExample" />.
    /// </summary>
    public PagedResult<DnsClientStaticAddressMappingDetail> GetExamples() =>
        new(
            [
                new DnsClientStaticAddressMappingDetail(
                    Guid.Parse("c3d4e5f6-3435-4637-a839-c3d4e5f6a7b3"),
                    "10.99.0.42/32",
                    "aa:bb:cc:dd:ee:ff",
                    InternetProtocolVersionEnum.IPv4,
                    false,
                    "Guest VLAN printer, static address, no DHCP relay configured there.",
                    DateTimeOffset.Parse("2026-09-18T09:00:00+00:00"),
                    null
                ),
            ],
            0,
            50,
            1
        );
}
