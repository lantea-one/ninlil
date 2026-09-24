using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Auth;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="TwoFactorLoginRequest" />, referenced by
///     <see cref="Controllers.AuthController.TwoFactorLogin" />.
/// </summary>
public class TwoFactorLoginRequestExample : IExamplesProvider<TwoFactorLoginRequest>
{
    /// <summary>
    ///     This method returns a completed authenticator-app challenge. The MFA token and code
    ///     values are both obviously-illustrative placeholders -- the token is short-lived and
    ///     single-use by design, and the code is a fixed, universally-recognizable six-digit
    ///     placeholder, not a value observed from a real device.
    /// </summary>
    public TwoFactorLoginRequest GetExamples() =>
        new("example-pending-mfa-token-4d5e6f7g8h9i-not-a-real-secret", "123456", "Authenticator");
}
