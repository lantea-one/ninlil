using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpServices;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.DhcpServiceControlController.List" /> -- one representative snapshot
///     showing every real combination this feature supports: a service both enabled at boot and
///     genuinely running (DHCPv4), a service disabled at boot but started live anyway (DHCPv6),
///     a service enabled at boot yet genuinely not running (Router Advertisement, stopped live),
///     and a service left at this project's own default, off, never-touched state (TFTP).
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
