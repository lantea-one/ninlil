using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Auth;

/// <summary>
///     This class supplies the Swagger example for <see cref="ConfirmPasswordResetRequest" />, the
///     request body of <see cref="Controllers.AuthController.ResetPassword" />.
/// </summary>
public class ConfirmPasswordResetRequestExample : IExamplesProvider<ConfirmPasswordResetRequest>
{
    /// <summary>
    ///     This method returns a reset submission as the "set new password" page sends it. The
    ///     token is a Base64Url-shaped placeholder that decodes to text saying it is not a real
    ///     secret.
    /// </summary>
    public ConfirmPasswordResetRequest GetExamples() =>
        new(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "Q2ZESjhLTDNQTjdWVCttTG9uZy1leGFtcGxlLXRva2VuLW5vdC1hLXJlYWwtc2VjcmV0",
            "EvenStrongerPassphrase2!"
        );
}
