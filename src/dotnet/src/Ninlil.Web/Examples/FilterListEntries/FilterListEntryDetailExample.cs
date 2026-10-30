using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.FilterListEntries;

/// <summary>
///     This class supplies the Swagger examples for <see cref="FilterListEntryDetail" />, as
///     returned by <see cref="Controllers.FilterListEntriesController.Get" /> and
///     <see cref="Controllers.FilterListEntriesController.Create" />.
/// </summary>
public class FilterListEntryDetailExample : IMultipleExamplesProvider<FilterListEntryDetail>
{
    /// <summary>
    ///     This method returns the three entries <see cref="FilterListEntryWriteRequestExample" />
    ///     submits, as the server reports them, plus an <c>IsException</c> entry of the kind only a
    ///     subscribed list's refresh creates. The first three carry the identifier of the "Internal
    ///     Allowlist" and the exception entry that of the "Hagezi Pro Blocklist", both from
    ///     <see cref="FilterLists.FilterListDetailExample" />: only a subscribed list has
    ///     exceptions.
    /// </summary>
    public IEnumerable<SwaggerExample<FilterListEntryDetail>> GetExamples() =>
        [
            new()
            {
                Name = "Exact domain",
                Summary = "Matches the exact domain and every one of its subdomains.",
                Value = new FilterListEntryDetail(
                    Guid.Parse("1c2d3e4f-5a6b-4c7d-8e9f-0a1b2c3d4e01"),
                    Guid.Parse("3f2504e0-4f89-4f6a-b8c1-f1a5e3a1b111"),
                    "ads.example.net",
                    "Known ad-network domain.",
                    DnsFilterEntryKindEnum.Domain,
                    DateTimeOffset.Parse("2026-07-01T08:10:00+00:00")
                ),
            },
            new()
            {
                Name = "Wildcard glob",
                Summary =
                    "Glob syntax, '*' for any run of characters and '?' for one, matched against the full queried name.",
                Value = new FilterListEntryDetail(
                    Guid.Parse("2d3e4f5a-6b7c-4d8e-9f0a-1b2c3d4e5f02"),
                    Guid.Parse("3f2504e0-4f89-4f6a-b8c1-f1a5e3a1b111"),
                    "*.ads.example.net",
                    "Matches every ad-network subdomain, however deep.",
                    DnsFilterEntryKindEnum.Wildcard,
                    DateTimeOffset.Parse("2026-07-01T08:11:00+00:00")
                ),
            },
            new()
            {
                Name = "Regular expression",
                Summary = "Regular expression syntax, matched against the full queried name.",
                Value = new FilterListEntryDetail(
                    Guid.Parse("3e4f5a6b-7c8d-4e9f-0a1b-2c3d4e5f6a03"),
                    Guid.Parse("3f2504e0-4f89-4f6a-b8c1-f1a5e3a1b111"),
                    """^track\d+\.example\.net$""",
                    "Matches every numbered tracking subdomain.",
                    DnsFilterEntryKindEnum.Regex,
                    DateTimeOffset.Parse("2026-07-01T08:12:00+00:00")
                ),
            },
            new()
            {
                Name = "Subscribed-list exception",
                Summary =
                    "An adblock '@@||cdn.ads.example.net^' line from a subscribed list: lets this name "
                    + "(and its subdomains) through despite the same list's broader rule.",
                Value = new FilterListEntryDetail(
                    Guid.Parse("4f5a6b7c-8d9e-4f0a-1b2c-3d4e5f6a7b04"),
                    Guid.Parse("8f14e45f-ceea-4b7f-9e33-1f7c2f4a2222"),
                    "cdn.ads.example.net",
                    null,
                    DnsFilterEntryKindEnum.Domain,
                    DateTimeOffset.Parse("2026-07-01T08:13:00+00:00"),
                    IsException: true
                ),
            },
        ];
}
