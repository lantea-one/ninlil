using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.TsigKeys;

/// <summary>
///     This class supplies the Swagger example for <see cref="TsigKeyCreateRequest" />, the request
///     body of <see cref="Controllers.TsigKeysController.Create" />.
/// </summary>
public class TsigKeyCreateRequestExample : IExamplesProvider<TsigKeyCreateRequest>
{
    /// <summary>
    ///     This method returns a request without a secret, so the server generates a random one,
    ///     the usual case.
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
