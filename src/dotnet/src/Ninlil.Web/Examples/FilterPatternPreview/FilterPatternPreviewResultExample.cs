using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.FilterPatternPreview;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="FilterPatternPreviewResult" />, referenced by
///     <see cref="Controllers.FilterPatternPreviewController.Preview" />.
/// </summary>
public class FilterPatternPreviewResultExample : IExamplesProvider<FilterPatternPreviewResult>
{
    /// <summary>
    ///     This method returns the real match outcome for <c>*.doubleclick.net</c> against this
    ///     controller's own default sample set -- only <c>doubleclick.net</c> itself matches (a
    ///     wildcard glob's <c>*</c> matches zero or more characters, so the bare domain matches
    ///     too), every other sample domain does not.
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
