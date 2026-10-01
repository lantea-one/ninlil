using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Auth;

/// <summary>
///     This class supplies the Swagger example for <see cref="LoginResponse" />, as returned by
///     <see cref="Controllers.AuthController.Login" /> and
///     <see cref="Controllers.AuthController.TwoFactorLogin" />.
/// </summary>
public class LoginResponseExample : IExamplesProvider<LoginResponse>
{
    /// <summary>
    ///     This method returns the completed sign-in shape. (The other shape sets
    ///     <see cref="LoginResponse.RequiresTwoFactor" /> and <see cref="LoginResponse.MfaToken" />
    ///     and leaves the token fields <see langword="null" />.) Both tokens are obvious
    ///     placeholders: a real access token is a signed JSON Web Token, and a realistic one in the
    ///     documentation could be mistaken for a leaked credential.
    /// </summary>
    public LoginResponse GetExamples() =>
        new(
            false,
            null,
            "eyJhbGciOiJIUzI1NiJ9.EXAMPLE_ACCESS_TOKEN_PAYLOAD_NOT_A_REAL_TOKEN.EXAMPLE_SIGNATURE_NOT_A_REAL_TOKEN",
            DateTimeOffset.Parse("2027-01-01T00:15:00+00:00"),
            "example-refresh-token-3f9a1c8b7d2e4f5061728394a5b6c7d8-not-a-real-secret"
        );
}
