using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Auth;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="ForgotPasswordResult" />, referenced by
///     <see cref="Controllers.AuthController.ForgotPassword" />. This is the one, always-identical
///     response that action returns -- see its own doc comment for why -- so this example is the
///     literal, real response shape a caller receives every time, not merely an illustrative
///     "success" case.
/// </summary>
public class ForgotPasswordResultExample : IExamplesProvider<ForgotPasswordResult>
{
    /// <summary>
    ///     This method returns the real, always-identical Forgot Password response.
    /// </summary>
    public ForgotPasswordResult GetExamples() =>
        new(
            "If an account exists for that e-mail address, a password reset link has been sent to it."
        );
}
