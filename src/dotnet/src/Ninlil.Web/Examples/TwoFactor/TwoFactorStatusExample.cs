using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.TwoFactor;

/// <summary>
///     This class supplies the Swagger example for <see cref="TwoFactorStatus" />, as returned by
///     <see cref="Controllers.TwoFactorController.GetStatus" />.
/// </summary>
public class TwoFactorStatusExample : IExamplesProvider<TwoFactorStatus>
{
    /// <summary>
    ///     This method returns an enrolled account with an authenticator, a confirmed e-mail
    ///     address and 8 of its 10 recovery codes left.
    /// </summary>
    public TwoFactorStatus GetExamples() => new(true, true, true, 8);
}
