using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.TwoFactor;

/// <summary>
///     This class supplies the Swagger example for <see cref="RecoveryCodesResult" />, as returned
///     by <see cref="Controllers.TwoFactorController.GenerateRecoveryCodes" />.
/// </summary>
public class RecoveryCodesResultExample : IExamplesProvider<RecoveryCodesResult>
{
    /// <summary>
    ///     This method returns a batch of 10 placeholder codes. Real codes are shown only once,
    ///     like a TSIG secret, so a real-looking batch does not belong in documentation.
    /// </summary>
    public RecoveryCodesResult GetExamples() =>
        new([
            "3f9a1c8b",
            "7d2e4f50",
            "61728394",
            "a5b6c7d8",
            "e9f0a1b2",
            "c3d4e5f6",
            "07182930",
            "4a5b6c7d",
            "8e9f0a1b",
            "2c3d4e5f",
        ]);
}
