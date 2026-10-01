using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ApiTokens;

/// <summary>
///     This class supplies the Swagger example for <see cref="CreateApiTokenRequest" />, the
///     request body of <see cref="Controllers.ApiTokensController.Create" />.
/// </summary>
public class CreateApiTokenRequestExample : IExamplesProvider<CreateApiTokenRequest>
{
    /// <summary>
    ///     This method returns a request for a static, long-lived token for an unattended
    ///     integration, the usual reason to create one.
    /// </summary>
    public CreateApiTokenRequest GetExamples() =>
        new("Dynamic DNS update client", false, DateTimeOffset.Parse("2027-08-31T00:00:00+00:00"));
}
