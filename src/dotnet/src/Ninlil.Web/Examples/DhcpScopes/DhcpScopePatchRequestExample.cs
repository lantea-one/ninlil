using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpScopes;

/// <summary>
///     This class supplies the Swagger example for the RFC 7396 merge patch body of
///     <see cref="Controllers.DhcpScopesController.Patch" />. The body is a
///     <see cref="JsonElement" /> because a merge patch must tell an absent field, which is left as
///     it is, from a present one; no typed model can express that.
/// </summary>
public class DhcpScopePatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that narrows the scope's range by moving its start and
    ///     disables the scope, leaving every other field as it is.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument
            .Parse(
                """
                { "rangeStartAddress": "192.168.1.150", "isDisabled": true }
                """
            )
            .RootElement.Clone();
}
