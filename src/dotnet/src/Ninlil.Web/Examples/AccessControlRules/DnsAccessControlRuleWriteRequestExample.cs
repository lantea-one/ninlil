using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.AccessControlRules;

/// <summary>
///     This class supplies the Swagger examples for
///     <see cref="DnsAccessControlRuleWriteRequest" />, the request body of
///     <see cref="Controllers.DnsAccessControlRulesController.Create" /> and
///     <see cref="Controllers.DnsAccessControlRulesController.Replace" />.
/// </summary>
public class DnsAccessControlRuleWriteRequestExample
    : IMultipleExamplesProvider<DnsAccessControlRuleWriteRequest>
{
    /// <summary>
    ///     This method returns two rules that between them use both capability names: a trusted /24
    ///     granted both, and a single /32 host granted only the rate-limit bypass. A single host is
    ///     written as /32 because a bare address is rejected.
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
