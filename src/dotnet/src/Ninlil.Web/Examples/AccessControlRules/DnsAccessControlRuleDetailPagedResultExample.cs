using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.AccessControlRules;

/// <summary>
///     This class supplies the Swagger example for a page of
///     <see cref="DnsAccessControlRuleDetail" />, as returned by
///     <see cref="Controllers.DnsAccessControlRulesController.List" />.
/// </summary>
public class DnsAccessControlRuleDetailPagedResultExample
    : IExamplesProvider<PagedResult<DnsAccessControlRuleDetail>>
{
    /// <summary>
    ///     This method returns one page holding the two rules
    ///     <see cref="DnsAccessControlRuleDetailExample" /> shows, with the default page size of
    ///     50.
    /// </summary>
    public PagedResult<DnsAccessControlRuleDetail> GetExamples() =>
        new(
            [
                new DnsAccessControlRuleDetail(
                    Guid.Parse("a1b2c3d4-1213-4415-8617-a1b2c3d4e5f1"),
                    "Trusted LAN",
                    "192.168.1.0/24",
                    ["RecursionAccess", "RateLimitBypass"],
                    false,
                    DateTimeOffset.Parse("2026-06-15T10:00:00+00:00"),
                    null
                ),
                new DnsAccessControlRuleDetail(
                    Guid.Parse("b2c3d4e5-2324-4526-9728-b2c3d4e5f6a2"),
                    "Monitoring host",
                    "192.168.1.50/32",
                    ["RateLimitBypass"],
                    false,
                    DateTimeOffset.Parse("2026-06-15T10:01:00+00:00"),
                    null
                ),
            ],
            0,
            50,
            2
        );
}
