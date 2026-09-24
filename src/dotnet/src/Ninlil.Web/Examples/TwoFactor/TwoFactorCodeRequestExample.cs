using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.TwoFactor;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="TwoFactorCodeRequest" />, referenced by
///     <see cref="Controllers.TwoFactorController.EnableAuthenticator" /> and
///     <see cref="Controllers.TwoFactorController.Disable" />, both of which accept this same
///     request shape.
/// </summary>
public class TwoFactorCodeRequestExample : IExamplesProvider<TwoFactorCodeRequest>
{
    /// <summary>
    ///     This method returns a fixed, universally-recognizable six-digit placeholder code, not a
    ///     value observed from a real authenticator device.
    /// </summary>
    public TwoFactorCodeRequest GetExamples() => new("123456");
}
