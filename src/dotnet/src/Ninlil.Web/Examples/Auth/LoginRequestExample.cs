using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Auth;

/// <summary>
///     This class supplies the Swagger example for <see cref="LoginRequest" />, the request body of
///     <see cref="Controllers.AuthController.Login" />.
/// </summary>
public class LoginRequestExample : IExamplesProvider<LoginRequest>
{
    /// <summary>
    ///     This method returns a sign-in request with the default <c>Body</c> refresh token
    ///     delivery. The password is an obvious example; it is a credential the caller supplies,
    ///     not a server-generated secret.
    /// </summary>
    public LoginRequest GetExamples() => new("jsmith", "CorrectHorseBatteryStaple1!");
}
