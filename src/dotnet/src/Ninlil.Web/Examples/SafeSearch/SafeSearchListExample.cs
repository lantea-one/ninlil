using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.SafeSearch;

/// <summary>
///     This class supplies the Swagger example for the catalog list returned by
///     <see cref="Controllers.SafeSearchController.List" />.
/// </summary>
public class SafeSearchListExample : IExamplesProvider<IReadOnlyList<SafeSearchDetail>>
{
    /// <summary>
    ///     This method returns two of the four catalog entries, as the catalog has them: Google
    ///     Search enabled and YouTube Restricted Mode not enabled.
    /// </summary>
    public IReadOnlyList<SafeSearchDetail> GetExamples() =>
        [
            new SafeSearchDetail(
                "google",
                "Google Search",
                "Forces Google's own SafeSearch filtering for every client on this network.",
                "https://support.google.com/websearch/answer/186669",
                2,
                true
            ),
            new SafeSearchDetail(
                "youtube",
                "YouTube Restricted Mode",
                "Forces YouTube's own strict Restricted Mode content filtering for every client on this network.",
                "https://support.google.com/a/answer/6214622",
                5,
                false
            ),
        ];
}
