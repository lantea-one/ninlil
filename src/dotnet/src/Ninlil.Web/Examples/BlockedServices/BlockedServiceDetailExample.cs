using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.BlockedServices;

/// <summary>
///     This class supplies the Swagger example for <see cref="BlockedServiceDetail" />, as returned
///     by <see cref="Controllers.BlockedServicesController.Enable" />.
/// </summary>
public class BlockedServiceDetailExample : IExamplesProvider<BlockedServiceDetail>
{
    /// <summary>
    ///     This method returns the TikTok catalog entry just enabled, with the identifier of its
    ///     managed filter list.
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
