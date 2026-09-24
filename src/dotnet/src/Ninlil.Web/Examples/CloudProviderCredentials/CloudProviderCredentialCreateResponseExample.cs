using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.CloudProviderCredentials;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="CloudProviderCredentialCreateResponse" />, referenced by
///     <see cref="Controllers.CloudProviderCredentialsController.Create" />.
/// </summary>
public class CloudProviderCredentialCreateResponseExample
    : IMultipleExamplesProvider<CloudProviderCredentialCreateResponse>
{
    /// <summary>
    ///     This method returns the two provider shapes
    ///     <see cref="CloudProviderCredentialCreateRequestExample" /> submits, alongside their own
    ///     plaintext secret material. Every value in
    ///     <see cref="CloudProviderCredentialCreateResponse.Secrets" /> here is an
    ///     obviously-fake placeholder -- this response is the one and only place a real
    ///     credential set's own secret material is ever returned, per
    ///     <see cref="CloudProviderCredentialDetail" />'s own doc comment, so a documented example
    ///     must never look like it could be mistaken for one.
    /// </summary>
    public IEnumerable<SwaggerExample<CloudProviderCredentialCreateResponse>> GetExamples() =>
        [
            new()
            {
                Name = "Cloudflare",
                Summary = "A single bearer-style API token, revealed exactly once.",
                Value = new CloudProviderCredentialCreateResponse(
                    new CloudProviderCredentialDetail(
                        Guid.Parse("6f708192-a3b4-4c5d-96e7-f8091a2b3c4d"),
                        "cloudflare-primary-account",
                        CloudDnsProviderTypeEnum.Cloudflare,
                        false,
                        "Zone editing token, scoped to the example.com zone only.",
                        DateTimeOffset.Parse("2026-08-01T09:00:00+00:00"),
                        null
                    ),
                    new Dictionary<string, string>
                    {
                        ["ApiToken"] =
                            "example-cloudflare-api-token-not-a-real-secret-3f9a1c8b7d2e4f50",
                    }
                ),
            },
            new()
            {
                Name = "Route 53",
                Summary =
                    "A paired AWS access key ID and secret access key, revealed exactly once.",
                Value = new CloudProviderCredentialCreateResponse(
                    new CloudProviderCredentialDetail(
                        Guid.Parse("708192a3-b4c5-4d6e-a7f8-091a2b3c4d5e"),
                        "route53-primary-account",
                        CloudDnsProviderTypeEnum.Route53,
                        false,
                        "IAM user scoped to route53:ChangeResourceRecordSets only.",
                        DateTimeOffset.Parse("2026-08-05T13:00:00+00:00"),
                        null
                    ),
                    new Dictionary<string, string>
                    {
                        ["AccessKeyId"] = "AKIAIOSFODNN7EXAMPLE",
                        ["SecretAccessKey"] = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
                    }
                ),
            },
        ];
}
