using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.SafeSearch;

/// <summary>
///     This class supplies the Swagger example for <see cref="SafeSearchDetail" />, as returned by
///     <see cref="Controllers.SafeSearchController.Enable" />.
/// </summary>
public class SafeSearchDetailExample : IExamplesProvider<SafeSearchDetail>
{
    /// <summary>
    ///     This method returns the Google Search entry, as the catalog has it, just enabled.
    /// </summary>
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
