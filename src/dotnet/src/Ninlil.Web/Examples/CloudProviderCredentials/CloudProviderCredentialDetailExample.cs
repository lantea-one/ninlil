using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.CloudProviderCredentials;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="CloudProviderCredentialDetail" />, referenced by
///     <see cref="Controllers.CloudProviderCredentialsController.Get" />.
/// </summary>
public class CloudProviderCredentialDetailExample : IExamplesProvider<CloudProviderCredentialDetail>
{
    /// <summary>
    ///     This method returns a credential set's own metadata with no secret material at all, per
    ///     <see cref="CloudProviderCredentialDetail" />'s own doc comment -- see
    ///     <see cref="CloudProviderCredentialCreateResponseExample" /> for the one response shape
    ///     that does include one.
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
