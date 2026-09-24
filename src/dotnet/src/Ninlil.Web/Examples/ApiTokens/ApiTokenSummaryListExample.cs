using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ApiTokens;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for the list of
///     <see cref="ApiTokenSummary" /> values <see cref="Controllers.ApiTokensController.List" />
///     returns -- never any token's own secret value, per <see cref="ApiTokenSummary" />'s own
///     doc comment.
/// </summary>
public class ApiTokenSummaryListExample : IExamplesProvider<IReadOnlyList<ApiTokenSummary>>
{
    /// <summary>
    ///     This method returns two representative tokens: a still-active static token, and a
    ///     one-time token that has already been consumed.
    /// </summary>
    public IReadOnlyList<ApiTokenSummary> GetExamples() =>
        [
            new ApiTokenSummary(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "Dynamic DNS update client",
                ApiTokenTypeEnum.Static,
                DateTimeOffset.Parse("2026-08-31T00:00:00+00:00"),
                DateTimeOffset.Parse("2027-08-31T00:00:00+00:00"),
                null,
                null
            ),
            new ApiTokenSummary(
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                "One-time provisioning script",
                ApiTokenTypeEnum.OneTime,
                DateTimeOffset.Parse("2026-08-30T00:00:00+00:00"),
                null,
                DateTimeOffset.Parse("2026-08-30T00:05:00+00:00"),
                null
            ),
        ];
}
