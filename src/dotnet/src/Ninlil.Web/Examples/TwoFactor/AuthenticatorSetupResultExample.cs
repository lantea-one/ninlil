using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.TwoFactor;

/// <summary>
///     This class supplies the Swagger example for <see cref="AuthenticatorSetupResult" />, as
///     returned by <see cref="Controllers.TwoFactorController.SetUpAuthenticator" />.
/// </summary>
public class AuthenticatorSetupResultExample : IExamplesProvider<AuthenticatorSetupResult>
{
    /// <summary>
    ///     This method returns a new TOTP enrollment. The shared key <c>JBSWY3DPEHPK3PXP</c> is the
    ///     demonstration secret many TOTP tutorials and libraries publish, so it cannot be mistaken
    ///     for a leaked one.
    /// </summary>
    public AuthenticatorSetupResult GetExamples() =>
        new(
            "JBSWY3DPEHPK3PXP",
            "otpauth://totp/Ninlil.Dns:jsmith?secret=JBSWY3DPEHPK3PXP&issuer=Ninlil.Dns&digits=6"
        );
}
