using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ApiTokens;

/// <summary>
///     This class supplies the Swagger example for <see cref="CreateApiTokenResponse" />, as
///     returned by <see cref="Controllers.ApiTokensController.Create" />.
/// </summary>
public class CreateApiTokenResponseExample : IExamplesProvider<CreateApiTokenResponse>
{
    /// <summary>
    ///     This method returns a newly issued token. <see cref="CreateApiTokenResponse.RawToken" />
    ///     is an obvious placeholder rather than something shaped like a real base64url secret, so
    ///     the documentation cannot be mistaken for a leaked token.
    /// </summary>
    public CreateApiTokenResponse GetExamples() =>
        new(
            "example-api-token-not-a-real-secret-3f9a1c8b7d2e4f5061728394a5b6c7d8",
            new ApiTokenSummary(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "Dynamic DNS update client",
                ApiTokenTypeEnum.Static,
                DateTimeOffset.Parse("2026-08-31T00:00:00+00:00"),
                DateTimeOffset.Parse("2027-08-31T00:00:00+00:00"),
                null,
                null
            )
        );
}
