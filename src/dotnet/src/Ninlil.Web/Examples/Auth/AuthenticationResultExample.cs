using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Auth;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="AuthenticationResult" />, referenced by
///     <see cref="Controllers.AuthController.Refresh" />.
/// </summary>
public class AuthenticationResultExample : IExamplesProvider<AuthenticationResult>
{
    /// <summary>
    ///     This method returns a freshly-rotated access/refresh token pair. Both values are
    ///     obviously-fake placeholder strings, for the identical reason
    ///     <see cref="LoginResponseExample" /> already documents.
    /// </summary>
    public AuthenticationResult GetExamples() =>
        new(
            "eyJhbGciOiJIUzI1NiJ9.EXAMPLE_ACCESS_TOKEN_PAYLOAD_NOT_A_REAL_TOKEN.EXAMPLE_SIGNATURE_NOT_A_REAL_TOKEN",
            DateTimeOffset.Parse("2027-01-01T00:15:00+00:00"),
            "example-refresh-token-8e9f0a1b2c3d4e5f6071829304a5b6c7-not-a-real-secret"
        );
}
