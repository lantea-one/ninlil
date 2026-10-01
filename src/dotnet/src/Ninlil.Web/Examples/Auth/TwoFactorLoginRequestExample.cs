using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Auth;

/// <summary>
///     This class supplies the Swagger example for <see cref="TwoFactorLoginRequest" />, the
///     request body of <see cref="Controllers.AuthController.TwoFactorLogin" />.
/// </summary>
public class TwoFactorLoginRequestExample : IExamplesProvider<TwoFactorLoginRequest>
{
    /// <summary>
    ///     This method returns an authenticator app code submission. The MFA token is a placeholder
    ///     and <c>123456</c> is a stock example code.
    /// </summary>
    public TwoFactorLoginRequest GetExamples() =>
        new("example-pending-mfa-token-4d5e6f7g8h9i-not-a-real-secret", "123456", "Authenticator");
}
