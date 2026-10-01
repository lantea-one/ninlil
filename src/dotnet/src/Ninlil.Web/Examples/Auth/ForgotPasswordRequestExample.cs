using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Auth;

/// <summary>
///     This class supplies the Swagger example for <see cref="ForgotPasswordRequest" />, the
///     request body of <see cref="Controllers.AuthController.ForgotPassword" />.
/// </summary>
public class ForgotPasswordRequestExample : IExamplesProvider<ForgotPasswordRequest>
{
    /// <summary>
    ///     This method returns a Forgot Password request for one e-mail address.
    /// </summary>
    public ForgotPasswordRequest GetExamples() => new("jsmith@example.com");
}
