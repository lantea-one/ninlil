using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpServices;

/// <summary>
///     This class supplies the Swagger example for the response of
///     <see cref="Controllers.DhcpServiceControlController.List" />, showing each combination of
///     the two flags: DHCPv4 enabled and running, DHCPv6 disabled at boot but started since, Router
///     Advertisement enabled but stopped since, and TFTP at its default, off and not running.
/// </summary>
public class DhcpServiceStatusListExample : IExamplesProvider<IReadOnlyList<DhcpServiceStatus>>
{
    /// <inheritdoc />
    public IReadOnlyList<DhcpServiceStatus> GetExamples() =>
        [
            new DhcpServiceStatus(DhcpServiceKindEnum.Dhcpv4, Enabled: true, IsRunning: true),
            new DhcpServiceStatus(DhcpServiceKindEnum.Dhcpv6, Enabled: false, IsRunning: true),
            new DhcpServiceStatus(
                DhcpServiceKindEnum.RouterAdvertisement,
                Enabled: true,
                IsRunning: false
            ),
            new DhcpServiceStatus(DhcpServiceKindEnum.Tftp, Enabled: false, IsRunning: false),
        ];
}
