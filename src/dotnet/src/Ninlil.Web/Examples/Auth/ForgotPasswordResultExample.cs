using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Auth;

/// <summary>
///     This class supplies the Swagger example for <see cref="ForgotPasswordResult" />, as returned
///     by <see cref="Controllers.AuthController.ForgotPassword" />. The action returns the same
///     response every time, so this example is the response a caller always gets.
/// </summary>
public class ForgotPasswordResultExample : IExamplesProvider<ForgotPasswordResult>
{
    /// <summary>
    ///     This method returns the fixed Forgot Password response.
    /// </summary>
    public ForgotPasswordResult GetExamples() =>
        new(
            "If an account exists for that e-mail address, a password reset link has been sent to it."
        );
}
