using Ninlil.Dhcp.Common.OptionCatalog;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpOptionCatalog;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.DhcpOptionCatalogController.List" /> -- a representative slice of the
///     DHCPv4 catalog showing several different <see cref="DhcpOptionValueTypeEnum" /> shapes side
///     by side, including one <see cref="DhcpOptionCatalogEntryDetail.IsServerAddressEligible" />
///     entry.
/// </summary>
public class DhcpOptionCatalogEntryDetailListExample
    : IExamplesProvider<IReadOnlyList<DhcpOptionCatalogEntryDetail>>
{
    /// <inheritdoc />
    public IReadOnlyList<DhcpOptionCatalogEntryDetail> GetExamples() =>
        [
            new DhcpOptionCatalogEntryDetail(
                1,
                "Subnet Mask",
                "RFC 2132 §3.3 -- the client's own subnet mask, as a dotted-quad IPv4 address.",
                DhcpOptionValueTypeEnum.IPv4Address,
                IsServerAddressEligible: false
            ),
            new DhcpOptionCatalogEntryDetail(
                6,
                "Domain Name Server",
                "RFC 2132 §3.8 -- one or more recursive DNS server addresses, in order of preference.",
                DhcpOptionValueTypeEnum.IPv4AddressList,
                IsServerAddressEligible: true
            ),
            new DhcpOptionCatalogEntryDetail(
                51,
                "IP Address Lease Time",
                "RFC 2132 §9.2 -- the duration, in seconds, of the granted lease.",
                DhcpOptionValueTypeEnum.Duration,
                IsServerAddressEligible: false
            ),
            new DhcpOptionCatalogEntryDetail(
                43,
                "Vendor Specific Information",
                "RFC 2132 §8.4 -- opaque, vendor-defined binary information.",
                DhcpOptionValueTypeEnum.OpaqueHex,
                IsServerAddressEligible: false
            ),
        ];
}
