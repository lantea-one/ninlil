using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.FilterListEntries;

/// <summary>
///     This class supplies the Swagger example for a page of <see cref="FilterListEntryDetail" />,
///     as returned by <see cref="Controllers.FilterListEntriesController.List" />.
/// </summary>
public class FilterListEntryDetailPagedResultExample
    : IExamplesProvider<PagedResult<FilterListEntryDetail>>
{
    /// <summary>
    ///     This method returns a page with one entry of each <see cref="DnsFilterEntryKindEnum" />,
    ///     the three from <see cref="FilterListEntryDetailExample" />.
    /// </summary>
    public PagedResult<FilterListEntryDetail> GetExamples() =>
        new(
            [
                new FilterListEntryDetail(
                    Guid.Parse("1c2d3e4f-5a6b-4c7d-8e9f-0a1b2c3d4e01"),
                    Guid.Parse("3f2504e0-4f89-4f6a-b8c1-f1a5e3a1b111"),
                    "ads.example.net",
                    "Known ad-network domain.",
                    DnsFilterEntryKindEnum.Domain,
                    DateTimeOffset.Parse("2026-07-01T08:10:00+00:00")
                ),
                new FilterListEntryDetail(
                    Guid.Parse("2d3e4f5a-6b7c-4d8e-9f0a-1b2c3d4e5f02"),
                    Guid.Parse("3f2504e0-4f89-4f6a-b8c1-f1a5e3a1b111"),
                    "*.ads.example.net",
                    "Matches every ad-network subdomain, however deep.",
                    DnsFilterEntryKindEnum.Wildcard,
                    DateTimeOffset.Parse("2026-07-01T08:11:00+00:00")
                ),
                new FilterListEntryDetail(
                    Guid.Parse("3e4f5a6b-7c8d-4e9f-0a1b-2c3d4e5f6a03"),
                    Guid.Parse("3f2504e0-4f89-4f6a-b8c1-f1a5e3a1b111"),
                    """^track\d+\.example\.net$""",
                    "Matches every numbered tracking subdomain.",
                    DnsFilterEntryKindEnum.Regex,
                    DateTimeOffset.Parse("2026-07-01T08:12:00+00:00")
                ),
            ],
            0,
            50,
            3
        );
}
