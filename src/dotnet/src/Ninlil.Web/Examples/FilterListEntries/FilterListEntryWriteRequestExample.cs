using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.FilterListEntries;

/// <summary>
///     This class supplies the Swagger examples for <see cref="FilterListEntryWriteRequest" />, the
///     request body of <see cref="Controllers.FilterListEntriesController.Create" /> and
///     <see cref="Controllers.FilterListEntriesController.Replace" />.
/// </summary>
public class FilterListEntryWriteRequestExample
    : IMultipleExamplesProvider<FilterListEntryWriteRequest>
{
    /// <summary>
    ///     This method returns one entry of each <see cref="DnsFilterEntryKindEnum" />, since the
    ///     syntax of <see cref="FilterListEntryWriteRequest.Value" /> depends on
    ///     <see cref="FilterListEntryWriteRequest.Kind" />. A wildcard or regex entry is accepted
    ///     only in a <see cref="DnsFilterListKindEnum.Domain" /> list (see
    ///     <see cref="Controllers.FilterListEntriesController" />).
    /// </summary>
    public IEnumerable<SwaggerExample<FilterListEntryWriteRequest>> GetExamples() =>
        [
            new()
            {
                Name = "Exact domain",
                Summary = "Matches the exact domain and every one of its subdomains.",
                Value = new FilterListEntryWriteRequest(
                    "ads.example.net",
                    "Known ad-network domain.",
                    DnsFilterEntryKindEnum.Domain
                ),
            },
            new()
            {
                Name = "Wildcard glob",
                Summary =
                    "Glob syntax, '*' for any run of characters and '?' for one, matched against the full queried name.",
                Value = new FilterListEntryWriteRequest(
                    "*.ads.example.net",
                    "Matches every ad-network subdomain, however deep.",
                    DnsFilterEntryKindEnum.Wildcard
                ),
            },
            new()
            {
                Name = "Regular expression",
                Summary = "Regular expression syntax, matched against the full queried name.",
                Value = new FilterListEntryWriteRequest(
                    """^track\d+\.example\.net$""",
                    "Matches every numbered tracking subdomain.",
                    DnsFilterEntryKindEnum.Regex
                ),
            },
        ];
}
