using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.TwoFactor;

/// <summary>
///     This class supplies the Swagger example for <see cref="TwoFactorCodeRequest" />, the request
///     body of <see cref="Controllers.TwoFactorController.EnableAuthenticator" /> and
///     <see cref="Controllers.TwoFactorController.Disable" />.
/// </summary>
public class TwoFactorCodeRequestExample : IExamplesProvider<TwoFactorCodeRequest>
{
    /// <summary>
    ///     This method returns the stock example code <c>123456</c>.
    /// </summary>
    public TwoFactorCodeRequest GetExamples() => new("123456");
}
