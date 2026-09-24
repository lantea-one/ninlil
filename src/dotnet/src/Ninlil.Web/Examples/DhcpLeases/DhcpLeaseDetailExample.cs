using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpLeases;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="DhcpLeaseDetail" />, referenced by <see cref="Controllers.DhcpLeasesController.Get" />.
/// </summary>
public class DhcpLeaseDetailExample : IExamplesProvider<DhcpLeaseDetail>
{
    /// <summary>
    ///     This method returns an actively-held lease in the "Home LAN" scope.
    /// </summary>
    public DhcpLeaseDetail GetExamples() =>
        new(
            Guid.Parse("f6a1b2c3-a3b4-4cd3-e5f6-a1b2c3d4e5f6"),
            InternetProtocolVersionEnum.IPv4,
            "Home LAN",
            "aa:bb:cc:dd:ee:ff",
            "192.168.1.142",
            "laptop-01",
            LeaseStateEnum.Active,
            DateTimeOffset.Parse("2026-06-10T08:00:00+00:00"),
            DateTimeOffset.Parse("2026-06-11T08:00:00+00:00"),
            DateTimeOffset.Parse("2026-06-10T08:00:00+00:00"),
            DateTimeOffset.Parse("2026-06-10T08:00:00+00:00"),
            null
        );
}
