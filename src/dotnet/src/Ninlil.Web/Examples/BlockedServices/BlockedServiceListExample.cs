using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.BlockedServices;

/// <summary>
///     This class supplies the Swagger example for the catalog list returned by
///     <see cref="Controllers.BlockedServicesController.List" />.
/// </summary>
public class BlockedServiceListExample : IExamplesProvider<IReadOnlyList<BlockedServiceDetail>>
{
    /// <summary>
    ///     This method returns two real catalog entries: TikTok enabled, with its managed filter
    ///     list identifier, and YouTube not enabled.
    /// </summary>
    public IReadOnlyList<BlockedServiceDetail> GetExamples() =>
        [
            new BlockedServiceDetail(
                "tiktok",
                "TikTok",
                "Blocks TikTok and its own supporting ByteDance-operated infrastructure domains.",
                "https://raw.githubusercontent.com/blocklistproject/Lists/master/tiktok.txt",
                true,
                Guid.Parse("8f14e45f-ceea-4b7f-9e33-1f7c2f4a3333")
            ),
            new BlockedServiceDetail(
                "youtube",
                "YouTube",
                "Blocks YouTube outright. An operator who wants YouTube reachable but restricted to its own safe-search-style content mode should use the Safe Search catalog instead of this one.",
                "https://raw.githubusercontent.com/blocklistproject/Lists/master/youtube.txt",
                false,
                null
            ),
        ];
}
