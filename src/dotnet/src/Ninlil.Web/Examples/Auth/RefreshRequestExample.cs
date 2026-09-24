using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Auth;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="RefreshRequest" />, referenced by
///     <see cref="Controllers.AuthController.Refresh" /> and
///     <see cref="Controllers.AuthController.Logout" />, both of which accept this same request
///     shape.
/// </summary>
public class RefreshRequestExample : IExamplesProvider<RefreshRequest>
{
    /// <summary>
    ///     This method returns a request naming an obviously-fake refresh token, the same
    ///     placeholder pattern this project's own login/refresh examples already establish.
    /// </summary>
    public RefreshRequest GetExamples() =>
        new("example-refresh-token-3f9a1c8b7d2e4f5061728394a5b6c7d8-not-a-real-secret");
}
