using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.CloudProviderCredentials;

/// <summary>
///     This class supplies the Swagger examples for
///     <see cref="CloudProviderCredentialCreateRequest" />, the request body of
///     <see cref="Controllers.CloudProviderCredentialsController.Create" />.
/// </summary>
public class CloudProviderCredentialCreateRequestExample
    : IMultipleExamplesProvider<CloudProviderCredentialCreateRequest>
{
    /// <summary>
    ///     This method returns the two credential shapes
    ///     <see cref="Ninlil.Dns.Server.Authoritative.CloudSync.CloudCredentialValidator" />
    ///     accepts: Cloudflare's single <c>ApiToken</c> (DigitalOcean uses the same) and Route 53's
    ///     <c>AccessKeyId</c> and <c>SecretAccessKey</c> pair. Every value is a placeholder; the
    ///     Route 53 pair is the example key pair AWS publishes in its own documentation.
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
