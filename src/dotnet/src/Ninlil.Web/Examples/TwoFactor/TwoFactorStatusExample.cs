using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.TwoFactor;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="TwoFactorStatus" />, referenced by
///     <see cref="Controllers.TwoFactorController.GetStatus" />.
/// </summary>
public class TwoFactorStatusExample : IExamplesProvider<TwoFactorStatus>
{
    /// <summary>
    ///     This method returns a fully-enrolled account with a confirmed authenticator, a
    ///     confirmed e-mail address, and a partially-consumed recovery code batch.
    /// </summary>
    public TwoFactorStatus GetExamples() => new(true, true, true, 8);
}
