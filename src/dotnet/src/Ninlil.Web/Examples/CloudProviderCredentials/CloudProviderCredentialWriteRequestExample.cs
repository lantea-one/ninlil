using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.CloudProviderCredentials;

/// <summary>
///     This class supplies the Swagger examples for
///     <see cref="CloudProviderCredentialWriteRequest" />, the request body of
///     <see cref="Controllers.CloudProviderCredentialsController.Replace" />.
/// </summary>
public class CloudProviderCredentialWriteRequestExample
    : IMultipleExamplesProvider<CloudProviderCredentialWriteRequest>
{
    /// <summary>
    ///     This method returns the two credential sets from
    ///     <see cref="CloudProviderCredentialCreateRequestExample" /> as full replacements, the
    ///     Cloudflare one with a rotated token. Unlike
    ///     <see cref="Controllers.TsigKeysController.Replace" />, a <c>PUT</c> here replaces the
    ///     stored secrets (see <see cref="CloudProviderCredentialWriteRequest" />).
    /// </summary>
    public IEnumerable<SwaggerExample<CloudProviderCredentialWriteRequest>> GetExamples() =>
        [
            new()
            {
                Name = "Cloudflare",
                Summary = "Rotating a single bearer-style API token.",
                Value = new CloudProviderCredentialWriteRequest(
                    "cloudflare-primary-account",
                    CloudDnsProviderTypeEnum.Cloudflare,
                    new Dictionary<string, string>
                    {
                        ["ApiToken"] =
                            "example-cloudflare-api-token-rotated-not-real-8b7d2e4f50a1c9",
                    },
                    false,
                    "Zone editing token, scoped to the example.com zone only. Rotated 2026-08-31."
                ),
            },
            new()
            {
                Name = "Route 53",
                Summary = "Rotating a paired AWS access key ID and secret access key.",
                Value = new CloudProviderCredentialWriteRequest(
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
