using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.TsigKeys;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="TsigKeyCreateRequest" />, referenced by
///     <see cref="Controllers.TsigKeysController.Create" />.
/// </summary>
public class TsigKeyCreateRequestExample : IExamplesProvider<TsigKeyCreateRequest>
{
    /// <summary>
    ///     This method returns a request that leaves <see cref="TsigKeyCreateRequest.Secret" />
    ///     unset, letting the server generate a cryptographically random one instead -- the more
    ///     common case, since there is rarely a reason for an operator to supply their own value.
    /// </summary>
    public TsigKeyCreateRequest GetExamples() =>
        new(
            "secondary-transfer-key",
            "hmac-sha256",
            null,
            false,
            "Used by branch-office.example.com to authenticate its own zone transfer."
        );
}
