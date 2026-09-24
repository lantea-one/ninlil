using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.AccessControlRules;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="DnsAccessControlRuleDetail" />, referenced by
///     <see cref="Controllers.DnsAccessControlRulesController.Get" /> and
///     <see cref="Controllers.DnsAccessControlRulesController.Create" />.
/// </summary>
public class DnsAccessControlRuleDetailExample
    : IMultipleExamplesProvider<DnsAccessControlRuleDetail>
{
    /// <summary>
    ///     This method returns the same two rules
    ///     <see cref="DnsAccessControlRuleWriteRequestExample" /> submits, as this server would
    ///     report them back -- a trusted LAN granted both capabilities at once, and a single
    ///     monitoring host granted only the rate-limit bypass.
    /// </summary>
    public IEnumerable<SwaggerExample<DnsAccessControlRuleDetail>> GetExamples() =>
        [
            new()
            {
                Name = "Trusted LAN",
                Summary =
                    "A /24 network granted both recursion access and a rate-limiting exemption.",
                Value = new DnsAccessControlRuleDetail(
                    Guid.Parse("a1b2c3d4-1213-4415-8617-a1b2c3d4e5f1"),
                    "Trusted LAN",
                    "192.168.1.0/24",
                    ["RecursionAccess", "RateLimitBypass"],
                    false,
                    DateTimeOffset.Parse("2026-06-15T10:00:00+00:00"),
                    null
                ),
            },
            new()
            {
                Name = "Monitoring host",
                Summary = "A single host exempted from rate limiting only.",
                Value = new DnsAccessControlRuleDetail(
                    Guid.Parse("b2c3d4e5-2324-4526-9728-b2c3d4e5f6a2"),
                    "Monitoring host",
                    "192.168.1.50/32",
                    ["RateLimitBypass"],
                    false,
                    DateTimeOffset.Parse("2026-06-15T10:01:00+00:00"),
                    null
                ),
            },
        ];
}
