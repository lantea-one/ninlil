using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.FilterListEntries;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="FilterListEntryWriteRequest" />, referenced by
///     <see cref="Controllers.FilterListEntriesController.Create" /> and
///     <see cref="Controllers.FilterListEntriesController.Replace" />.
/// </summary>
public class FilterListEntryWriteRequestExample
    : IMultipleExamplesProvider<FilterListEntryWriteRequest>
{
    /// <summary>
    ///     This method returns three genuinely different entry shapes -- one per
    ///     <see cref="DnsFilterEntryKindEnum" /> value -- since which syntax
    ///     <see cref="FilterListEntryWriteRequest.Value" /> is written in changes completely
    ///     depending on <see cref="FilterListEntryWriteRequest.Kind" />, and a caller reading only
    ///     the default <see cref="DnsFilterEntryKindEnum.Domain" /> shape would have no idea the
    ///     other two even exist. Every one of these three is only ever valid against a
    ///     <see cref="DnsFilterListKindEnum.Domain" />-kind list -- see
    ///     <see cref="Controllers.FilterListEntriesController" /> for why a
    ///     <see cref="DnsFilterEntryKindEnum.Wildcard" />/<see cref="DnsFilterEntryKindEnum.Regex" />
    ///     entry is rejected against any other list kind.
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
                    "AdGuard Home-style '*'/'?' glob syntax, matched against the full queried name.",
                Value = new FilterListEntryWriteRequest(
                    "*.ads.example.net",
                    "Matches every ad-network subdomain, however deep.",
                    DnsFilterEntryKindEnum.Wildcard
                ),
            },
            new()
            {
                Name = "Regular expression",
                Summary = "Pi-hole-style regex syntax, matched against the full queried name.",
                Value = new FilterListEntryWriteRequest(
                    """^track\d+\.example\.net$""",
                    "Matches every numbered tracking subdomain.",
                    DnsFilterEntryKindEnum.Regex
                ),
            },
        ];
}
