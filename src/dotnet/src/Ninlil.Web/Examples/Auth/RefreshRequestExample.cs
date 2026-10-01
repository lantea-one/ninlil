using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Auth;

/// <summary>
///     This class supplies the Swagger example for <see cref="RefreshRequest" />, the request body
///     of <see cref="Controllers.AuthController.Refresh" /> and
///     <see cref="Controllers.AuthController.Logout" />.
/// </summary>
public class RefreshRequestExample : IExamplesProvider<RefreshRequest>
{
    /// <summary>
    ///     This method returns a request naming a placeholder refresh token, matching the one in
    ///     <see cref="LoginResponseExample" />.
    /// </summary>
    public RefreshRequest GetExamples() =>
        new("example-refresh-token-3f9a1c8b7d2e4f5061728394a5b6c7d8-not-a-real-secret");
}
