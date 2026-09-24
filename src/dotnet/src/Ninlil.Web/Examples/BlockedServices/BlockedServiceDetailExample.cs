using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.BlockedServices;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="BlockedServiceDetail" />, referenced by
///     <see cref="Controllers.BlockedServicesController.Enable" />.
/// </summary>
public class BlockedServiceDetailExample : IExamplesProvider<BlockedServiceDetail>
{
    /// <summary>
    ///     This method returns TikTok freshly enabled, with its own managed filter list's
    ///     identifier already assigned.
    /// </summary>
    public BlockedServiceDetail GetExamples() =>
        new(
            "tiktok",
            "TikTok",
            "Blocks TikTok and its own supporting ByteDance-operated infrastructure domains.",
            "https://raw.githubusercontent.com/blocklistproject/Lists/master/tiktok.txt",
            true,
            Guid.Parse("8f14e45f-ceea-4b7f-9e33-1f7c2f4a3333")
        );
}
