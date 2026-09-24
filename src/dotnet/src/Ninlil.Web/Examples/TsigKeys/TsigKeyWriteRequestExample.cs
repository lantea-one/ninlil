using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.TsigKeys;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="TsigKeyWriteRequest" />, referenced by
///     <see cref="Controllers.TsigKeysController.Replace" />.
/// </summary>
public class TsigKeyWriteRequestExample : IExamplesProvider<TsigKeyWriteRequest>
{
    /// <summary>
    ///     This method returns a full replacement of a key's own metadata -- the key's own shared
    ///     secret has no field here at all; see <see cref="TsigKeyWriteRequest" />'s own doc
    ///     comment for why.
    /// </summary>
    public TsigKeyWriteRequest GetExamples() =>
        new(
            "secondary-transfer-key",
            "hmac-sha256",
            false,
            "Used by branch-office.example.com to authenticate its own zone transfer."
        );
}
