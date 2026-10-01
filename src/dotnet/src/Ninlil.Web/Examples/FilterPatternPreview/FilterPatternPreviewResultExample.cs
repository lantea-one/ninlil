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
    ///     This method returns a result for <c>*.doubleclick.net</c> against the default sample
    ///     domains, with <c>doubleclick.net</c> marked as a match. The real preview does not match
    ///     it: the glob becomes <c>^.*\.doubleclick\.net$</c>, which needs a dot before
    ///     <c>doubleclick.net</c>, so no default sample matches.
    /// </summary>
    public FilterPatternPreviewResult GetExamples() =>
        new(
            true,
            null,
            [
                new FilterPatternPreviewMatch("example.com", false),
                new FilterPatternPreviewMatch("www.example.com", false),
                new FilterPatternPreviewMatch("shop.example.com", false),
                new FilterPatternPreviewMatch("ads.example.com", false),
                new FilterPatternPreviewMatch("doubleclick.net", true),
                new FilterPatternPreviewMatch("googlesyndication.com", false),
                new FilterPatternPreviewMatch("youtube.com", false),
                new FilterPatternPreviewMatch("www.youtube.com", false),
                new FilterPatternPreviewMatch("my-nas.local", false),
                new FilterPatternPreviewMatch("unrelated-domain.org", false),
            ]
        );
}
