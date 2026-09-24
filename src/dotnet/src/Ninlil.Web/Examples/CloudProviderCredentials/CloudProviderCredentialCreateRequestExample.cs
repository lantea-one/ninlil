using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.CloudProviderCredentials;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="CloudProviderCredentialCreateRequest" />, referenced by
///     <see cref="Controllers.CloudProviderCredentialsController.Create" />.
/// </summary>
public class CloudProviderCredentialCreateRequestExample
    : IMultipleExamplesProvider<CloudProviderCredentialCreateRequest>
{
    /// <summary>
    ///     This method returns two genuinely different provider shapes -- Cloudflare's single
    ///     bearer-style <c>ApiToken</c> field, and Route 53's paired <c>AccessKeyId</c>/
    ///     <c>SecretAccessKey</c> fields -- so a caller reading this example sees exactly what
    ///     <see cref="Ninlil.Dns.Server.Authoritative.CloudSync.CloudCredentialValidator" /> actually requires
    ///     for each <see cref="CloudDnsProviderTypeEnum" />, rather than guessing at
    ///     <see cref="CloudProviderCredentialCreateRequest.Credentials" />'s own opaque shape.
    ///     Every value here is an obviously-fake placeholder, never a value shaped to look like a
    ///     real, genuinely-leakable credential -- Route 53's own pair reuses AWS's own widely
    ///     published documentation example credentials
    ///     (<c>AKIAIOSFODNN7EXAMPLE</c>/<c>wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY</c>), the exact
    ///     "reuse a universally-recognized demo value" precedent this project's own
    ///     <c>AuthenticatorSetupResultExample</c> already established for a TOTP shared key.
    /// </summary>
    public IEnumerable<SwaggerExample<CloudProviderCredentialCreateRequest>> GetExamples() =>
        [
            new()
            {
                Name = "Cloudflare",
                Summary = "A single bearer-style API token.",
                Value = new CloudProviderCredentialCreateRequest(
                    "cloudflare-primary-account",
                    CloudDnsProviderTypeEnum.Cloudflare,
                    new Dictionary<string, string>
                    {
                        ["ApiToken"] =
                            "example-cloudflare-api-token-not-a-real-secret-3f9a1c8b7d2e4f50",
                    },
                    false,
                    "Zone editing token, scoped to the example.com zone only."
                ),
            },
            new()
            {
                Name = "Route 53",
                Summary = "A paired AWS access key ID and secret access key.",
                Value = new CloudProviderCredentialCreateRequest(
                    "route53-primary-account",
                    CloudDnsProviderTypeEnum.Route53,
                    new Dictionary<string, string>
                    {
                        ["AccessKeyId"] = "AKIAIOSFODNN7EXAMPLE",
                        ["SecretAccessKey"] = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
                    },
                    false,
                    "IAM user scoped to route53:ChangeResourceRecordSets only."
                ),
            },
        ];
}
