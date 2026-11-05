using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Auth;

/// <summary>
///     This class supplies the Swagger example for <see cref="ConfirmEmailChangeRequest" />, the
///     request body of <see cref="Controllers.AuthController.ConfirmEmailChange" />.
/// </summary>
public class ConfirmEmailChangeRequestExample : IExamplesProvider<ConfirmEmailChangeRequest>
{
    /// <summary>
    ///     This method returns a confirmation as the "confirm e-mail change" page sends it. The
    ///     token is a Base64Url-shaped placeholder that decodes to text saying it is not a real
    ///     secret.
    /// </summary>
    public ConfirmEmailChangeRequest GetExamples() =>
        new(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "jamie.smith@example.com",
            "Q2ZESjhLTDNQTjdWVCttTG9uZy1leGFtcGxlLXRva2VuLW5vdC1hLXJlYWwtc2VjcmV0"
        );
}
