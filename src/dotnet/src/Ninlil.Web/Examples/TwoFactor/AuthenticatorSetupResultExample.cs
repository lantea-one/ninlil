using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.TwoFactor;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="AuthenticatorSetupResult" />, referenced by
///     <see cref="Controllers.TwoFactorController.SetUpAuthenticator" />.
/// </summary>
public class AuthenticatorSetupResultExample : IExamplesProvider<AuthenticatorSetupResult>
{
    /// <summary>
    ///     This method returns a freshly-generated TOTP enrollment. The shared key,
    ///     <c>JBSWY3DPEHPK3PXP</c>, is the widely-published, universally-recognized demonstration
    ///     secret used across countless real TOTP tutorials and libraries -- reused here
    ///     deliberately, rather than a freshly-invented-looking value, so nobody could ever mistake
    ///     it for a genuine, leaked authenticator secret.
    /// </summary>
    public AuthenticatorSetupResult GetExamples() =>
        new(
            "JBSWY3DPEHPK3PXP",
            "otpauth://totp/Ninlil.Dns:jsmith?secret=JBSWY3DPEHPK3PXP&issuer=Ninlil.Dns&digits=6"
        );
}
