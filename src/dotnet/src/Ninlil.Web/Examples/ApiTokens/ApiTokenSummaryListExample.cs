using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ApiTokens;

/// <summary>
///     This class supplies the Swagger example for the list of <see cref="ApiTokenSummary" />
///     returned by <see cref="Controllers.ApiTokensController.List" />, which never includes a
///     token's secret value.
/// </summary>
public class ApiTokenSummaryListExample : IExamplesProvider<IReadOnlyList<ApiTokenSummary>>
{
    /// <summary>
    ///     This method returns two tokens: an active static token with an expiry date, and a
    ///     one-time token that has been used.
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
