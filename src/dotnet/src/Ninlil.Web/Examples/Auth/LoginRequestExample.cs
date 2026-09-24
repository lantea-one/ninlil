using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Auth;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="LoginRequest" />, referenced by <see cref="Controllers.AuthController.Login" />
///     via a <c>SwaggerRequestExample</c> attribute.
/// </summary>
public class LoginRequestExample : IExamplesProvider<LoginRequest>
{
    /// <summary>
    ///     This method returns one realistic sign-in request. The password value is a plain,
    ///     obviously-illustrative example -- it is a caller-supplied credential the caller already
    ///     knows, not a server-generated secret this project needs to keep out of documentation.
    /// </summary>
    public LoginRequest GetExamples() => new("jsmith", "CorrectHorseBatteryStaple1!");
}
