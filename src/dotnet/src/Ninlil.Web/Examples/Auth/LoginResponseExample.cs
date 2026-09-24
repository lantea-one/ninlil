using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Auth;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="LoginResponse" />, referenced by
///     <see cref="Controllers.AuthController.Login" /> and
///     <see cref="Controllers.AuthController.TwoFactorLogin" />, both of which return this same
///     response shape.
/// </summary>
public class LoginResponseExample : IExamplesProvider<LoginResponse>
{
    /// <summary>
    ///     This method returns the completed-sign-in shape of <see cref="LoginResponse" /> (the
    ///     other real shape this endpoint can return sets <see cref="LoginResponse.RequiresTwoFactor" />
    ///     instead, with every other field <see langword="null" />, per that record's own doc
    ///     comment). Both the access token and refresh token values are obviously-fake placeholder
    ///     strings -- a real access token is a signed JSON Web Token this project must never
    ///     publish a genuine example of, since a reader could otherwise mistake a real leaked
    ///     credential for documentation.
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
