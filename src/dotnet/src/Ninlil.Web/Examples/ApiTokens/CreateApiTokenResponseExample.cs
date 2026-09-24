using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ApiTokens;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="CreateApiTokenResponse" />, referenced by
///     <see cref="Controllers.ApiTokensController.Create" />.
/// </summary>
public class CreateApiTokenResponseExample : IExamplesProvider<CreateApiTokenResponse>
{
    /// <summary>
    ///     This method returns a freshly-issued token. <see cref="CreateApiTokenResponse.RawToken" />
    ///     is an obviously-fake placeholder string, not a value shaped to look like a real,
    ///     genuinely-leakable base64url secret -- this response is the one and only place a real
    ///     token's raw value is ever returned, per that record's own doc comment, so a documented
    ///     example must never look like it could be mistaken for one.
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
