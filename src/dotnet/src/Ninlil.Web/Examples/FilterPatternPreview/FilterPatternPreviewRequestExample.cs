using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.FilterPatternPreview;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="FilterPatternPreviewRequest" />, referenced by
///     <see cref="Controllers.FilterPatternPreviewController.Preview" />.
/// </summary>
public class FilterPatternPreviewRequestExample : IExamplesProvider<FilterPatternPreviewRequest>
{
    /// <summary>This method returns a wildcard pattern previewed against the default sample set.</summary>
    public FilterPatternPreviewRequest GetExamples() =>
        new(DnsFilterEntryKindEnum.Wildcard, "*.doubleclick.net", null);
}
