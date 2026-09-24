using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ApiTokens;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="CreateApiTokenRequest" />, referenced by
///     <see cref="Controllers.ApiTokensController.Create" />.
/// </summary>
public class CreateApiTokenRequestExample : IExamplesProvider<CreateApiTokenRequest>
{
    /// <summary>
    ///     This method returns a static, long-lived token request for an unattended integration --
    ///     the most common real-world reason to mint one, per <see cref="CreateApiTokenRequest.OneTime" />'s
    ///     own doc comment.
    /// </summary>
    public CreateApiTokenRequest GetExamples() =>
        new("Dynamic DNS update client", false, DateTimeOffset.Parse("2027-08-31T00:00:00+00:00"));
}
