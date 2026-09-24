using Ninlil.Dhcp.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpScopeCustomOptions;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="DhcpScopeCustomOptionDetail" />, referenced by
///     <see cref="Controllers.DhcpScopeCustomOptionsController.Get" /> and
///     <see cref="Controllers.DhcpScopeCustomOptionsController.Create" />.
/// </summary>
public class DhcpScopeCustomOptionDetailExample : IExamplesProvider<DhcpScopeCustomOptionDetail>
{
    /// <summary>
    ///     This method returns the same TFTP Server Name option
    ///     <see cref="DhcpScopeCustomOptionWriteRequestExample" /> submits, as this server would
    ///     report it back.
    /// </summary>
    public DhcpScopeCustomOptionDetail GetExamples() =>
        new(
            Guid.Parse("c3d4e5f6-7081-49a0-b2c3-d4e5f6a1b2c3"),
            Guid.Parse("a1b2c3d4-5e6f-4708-9192-a3b4c5d6e7f1"),
            66,
            Convert.ToHexString("192.168.1.10"u8.ToArray()),
            false,
            DhcpOptionValueSourceEnum.Custom,
            "TFTP Server Name for PXE boot",
            DateTimeOffset.Parse("2026-06-01T09:15:00+00:00"),
            null
        );
}
