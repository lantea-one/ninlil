using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.CloudProviderCredentials;

/// <summary>
///     This class supplies the Swagger example for <see cref="CloudProviderCredentialDetail" />, as
///     returned by <see cref="Controllers.CloudProviderCredentialsController.Get" />.
/// </summary>
public class CloudProviderCredentialDetailExample : IExamplesProvider<CloudProviderCredentialDetail>
{
    /// <summary>
    ///     This method returns a Cloudflare credential set without its secrets; only
    ///     <see cref="CloudProviderCredentialCreateResponseExample" /> shows those.
    /// </summary>
    public CloudProviderCredentialDetail GetExamples() =>
        new(
            Guid.Parse("6f708192-a3b4-4c5d-96e7-f8091a2b3c4d"),
            "cloudflare-primary-account",
            CloudDnsProviderTypeEnum.Cloudflare,
            false,
            "Zone editing token, scoped to the example.com zone only.",
            DateTimeOffset.Parse("2026-08-01T09:00:00+00:00"),
            null
        );
}
