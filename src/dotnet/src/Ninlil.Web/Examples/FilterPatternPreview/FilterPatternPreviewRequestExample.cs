using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.FilterPatternPreview;

/// <summary>
///     This class supplies the Swagger example for <see cref="FilterPatternPreviewRequest" />, the
///     request body of <see cref="Controllers.FilterPatternPreviewController.Preview" />.
/// </summary>
public class FilterPatternPreviewRequestExample : IExamplesProvider<FilterPatternPreviewRequest>
{
    /// <summary>
    ///     This method returns a wildcard pattern tested against the default sample domains.
    /// </summary>
    public FilterPatternPreviewRequest GetExamples() =>
        new(DnsFilterEntryKindEnum.Wildcard, "*.doubleclick.net", null);
}
