using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.CloudProviderCredentials;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="CloudProviderCredentialWriteRequest" />, referenced by
///     <see cref="Controllers.CloudProviderCredentialsController.Replace" />.
/// </summary>
public class CloudProviderCredentialWriteRequestExample
    : IMultipleExamplesProvider<CloudProviderCredentialWriteRequest>
{
    /// <summary>
    ///     This method returns the same two provider shapes
    ///     <see cref="CloudProviderCredentialCreateRequestExample" /> submits, but as a full
    ///     replacement, including a genuinely rotated secret value -- unlike
    ///     <see cref="Controllers.TsigKeysController.Replace" />, a <c>PUT</c> here really does
    ///     replace the stored secret material, per <see cref="CloudProviderCredentialWriteRequest" />'s
    ///     own doc comment.
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
