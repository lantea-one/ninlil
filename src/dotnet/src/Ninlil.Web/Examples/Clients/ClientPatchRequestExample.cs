using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Clients;

/// <summary>
///     This class supplies the Swagger example for the RFC 7396 merge patch body of
///     <see cref="Controllers.ClientsController.Patch" />. The body is a <see cref="JsonElement" />
///     because a merge patch must tell an absent field, which is left as it is, from a present one;
///     no typed model can express that.
/// </summary>
public class ClientPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that renames the client and stars it, leaving every other
    ///     field, including its group and icon, as it is.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument
            .Parse("""{ "displayName": "Living Room TV (renamed)", "isFavorite": true }""")
            .RootElement.Clone();
}
