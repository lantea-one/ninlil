using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.SafeSearch;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="SafeSearchDetail" />, referenced by
///     <see cref="Controllers.SafeSearchController.Enable" />.
/// </summary>
public class SafeSearchDetailExample : IExamplesProvider<SafeSearchDetail>
{
    /// <summary>This method returns Google Search freshly enabled.</summary>
    public SafeSearchDetail GetExamples() =>
        new(
            "google",
            "Google Search",
            "Forces Google's own SafeSearch filtering for every client on this network.",
            "https://support.google.com/websearch/answer/186669",
            2,
            true
        );
}
