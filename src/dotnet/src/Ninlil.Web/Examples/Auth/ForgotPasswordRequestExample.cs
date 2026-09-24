using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Auth;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="ForgotPasswordRequest" />, referenced by
///     <see cref="Controllers.AuthController.ForgotPassword" />.
/// </summary>
public class ForgotPasswordRequestExample : IExamplesProvider<ForgotPasswordRequest>
{
    /// <summary>
    ///     This method returns one realistic Forgot Password request.
    /// </summary>
    public ForgotPasswordRequest GetExamples() => new("jsmith@example.com");
}
