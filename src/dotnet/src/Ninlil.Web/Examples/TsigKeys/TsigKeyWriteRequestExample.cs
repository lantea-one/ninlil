using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.TsigKeys;

/// <summary>
///     This class supplies the Swagger example for <see cref="TsigKeyWriteRequest" />, the request
///     body of <see cref="Controllers.TsigKeysController.Replace" />.
/// </summary>
public class TsigKeyWriteRequestExample : IExamplesProvider<TsigKeyWriteRequest>
{
    /// <summary>
    ///     This method returns a full replacement of a key's settings. There is no secret field
    ///     (see <see cref="TsigKeyWriteRequest" />).
    /// </summary>
    public TsigKeyWriteRequest GetExamples() =>
        new(
            "secondary-transfer-key",
            "hmac-sha256",
            false,
            "Used by branch-office.example.com to authenticate its own zone transfer."
        );
}
