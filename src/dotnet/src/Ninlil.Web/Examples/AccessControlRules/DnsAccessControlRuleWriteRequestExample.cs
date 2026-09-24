using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.AccessControlRules;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="DnsAccessControlRuleWriteRequest" />, referenced by
///     <see cref="Controllers.DnsAccessControlRulesController.Create" /> and
///     <see cref="Controllers.DnsAccessControlRulesController.Replace" />.
/// </summary>
public class DnsAccessControlRuleWriteRequestExample
    : IMultipleExamplesProvider<DnsAccessControlRuleWriteRequest>
{
    /// <summary>
    ///     This method returns two genuinely different capability combinations -- a trusted LAN
    ///     granted both capabilities at once, and a single monitoring host granted only the
    ///     rate-limit bypass -- exercising both this project's own real capability names.
    /// </summary>
    public IEnumerable<SwaggerExample<DnsAccessControlRuleWriteRequest>> GetExamples() =>
        [
            new()
            {
                Name = "Trusted LAN",
                Summary =
                    "A /24 network granted both recursion access and a rate-limiting exemption.",
                Value = new DnsAccessControlRuleWriteRequest(
                    "Trusted LAN",
                    "192.168.1.0/24",
                    ["RecursionAccess", "RateLimitBypass"],
                    IsDisabled: false
                ),
            },
            new()
            {
                Name = "Monitoring host",
                Summary = "A single host exempted from rate limiting only.",
                Value = new DnsAccessControlRuleWriteRequest(
                    "Monitoring host",
                    "192.168.1.50/32",
                    ["RateLimitBypass"],
                    IsDisabled: false
                ),
            },
        ];
}
