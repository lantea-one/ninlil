using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Auth;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="ConfirmPasswordResetRequest" />, referenced by
///     <see cref="Controllers.AuthController.ResetPassword" />.
/// </summary>
public class ConfirmPasswordResetRequestExample : IExamplesProvider<ConfirmPasswordResetRequest>
{
    /// <summary>
    ///     This method returns a realistic confirm-reset submission -- the token value is an
    ///     obviously-illustrative, Base64Url-shaped placeholder, exactly as it would arrive from
    ///     the shared "set new password" landing page's own query string.
    /// </summary>
    public ConfirmPasswordResetRequest GetExamples() =>
        new(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "Q2ZESjhLTDNQTjdWVCttTG9uZy1leGFtcGxlLXRva2VuLW5vdC1hLXJlYWwtc2VjcmV0",
            "EvenStrongerPassphrase2!"
        );
}
