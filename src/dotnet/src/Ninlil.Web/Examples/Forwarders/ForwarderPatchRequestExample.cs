using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Forwarders;

/// <summary>
///     This class supplies the Swagger example for the RFC 7396 merge patch body of
///     <see cref="Controllers.ForwardersController.Patch" />. The body is a
///     <see cref="JsonElement" /> because a merge patch must tell an absent field, which is left as
///     it is, from a present one; no typed model can express that.
/// </summary>
public class ForwarderPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that disables a forwarder while it is unreachable, with a
    ///     note, and leaves every other field as it is.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument
            .Parse(
                """{ "isDisabled": true, "comments": "Temporarily disabled while this resolver is unreachable." }"""
            )
            .RootElement.Clone();
}
