using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.FilterPatternPreview;

/// <summary>
///     This class supplies the Swagger example for <see cref="FilterPatternPreviewResult" />, as
///     returned by <see cref="Controllers.FilterPatternPreviewController.Preview" />.
/// </summary>
public class FilterPatternPreviewResultExample : IExamplesProvider<FilterPatternPreviewResult>
{
    /// <summary>
    ///     This method returns the result of <c>*.example.com</c> against the default sample
    ///     domains, as <see cref="FilterPatternPreviewRequestExample" /> submits it. The glob
    ///     becomes <c>^.*\.example\.com$</c>, which needs a dot before <c>example.com</c>, so the
    ///     three subdomains match and <c>example.com</c> does not.
    /// </summary>
    public FilterPatternPreviewResult GetExamples() =>
        new(
            true,
            null,
            [
                new FilterPatternPreviewMatch("example.com", false),
                new FilterPatternPreviewMatch("www.example.com", true),
                new FilterPatternPreviewMatch("shop.example.com", true),
                new FilterPatternPreviewMatch("ads.example.com", true),
                new FilterPatternPreviewMatch("doubleclick.net", false),
                new FilterPatternPreviewMatch("googlesyndication.com", false),
                new FilterPatternPreviewMatch("youtube.com", false),
                new FilterPatternPreviewMatch("www.youtube.com", false),
                new FilterPatternPreviewMatch("my-nas.local", false),
                new FilterPatternPreviewMatch("unrelated-domain.org", false),
            ]
        );
}
