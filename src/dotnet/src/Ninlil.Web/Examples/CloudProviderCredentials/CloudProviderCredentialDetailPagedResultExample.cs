using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.CloudProviderCredentials;

/// <summary>
///     This class supplies the Swagger example for a page of
///     <see cref="CloudProviderCredentialDetail" />, as returned by
///     <see cref="Controllers.CloudProviderCredentialsController.List" />.
/// </summary>
public class CloudProviderCredentialDetailPagedResultExample
    : IExamplesProvider<PagedResult<CloudProviderCredentialDetail>>
{
    /// <summary>
    ///     This method returns a page with one Cloudflare and one Route 53 credential set, neither
    ///     with secrets.
    /// </summary>
    public PagedResult<CloudProviderCredentialDetail> GetExamples() =>
        new(
            [
                new CloudProviderCredentialDetail(
                    Guid.Parse("6f708192-a3b4-4c5d-96e7-f8091a2b3c4d"),
                    "cloudflare-primary-account",
                    CloudDnsProviderTypeEnum.Cloudflare,
                    false,
                    "Zone editing token, scoped to the example.com zone only.",
                    DateTimeOffset.Parse("2026-08-01T09:00:00+00:00"),
                    null
                ),
                new CloudProviderCredentialDetail(
                    Guid.Parse("708192a3-b4c5-4d6e-a7f8-091a2b3c4d5e"),
                    "route53-primary-account",
                    CloudDnsProviderTypeEnum.Route53,
                    false,
                    "IAM user scoped to route53:ChangeResourceRecordSets only.",
                    DateTimeOffset.Parse("2026-08-05T13:00:00+00:00"),
                    null
                ),
            ],
            0,
            50,
            2
        );
}
