using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Clients;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.ClientsController.Patch" />'s own RFC 7396 JSON Merge Patch request
///     body. The request parameter's own declared type is the generic <see cref="JsonElement" />,
///     not a dedicated model class -- a field this project's merge patch endpoints leave out of
///     the request body entirely is left untouched, which no strongly-typed C# model can
///     represent.
/// </summary>
public class ClientPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that renames the client and stars it as a favorite, leaving
    ///     every other field -- including its group assignment and device icon -- untouched.
    ///     <see cref="JsonDocument.RootElement" /> is cloned so the returned
    ///     <see cref="JsonElement" /> stays valid independently of the parsed
    ///     <see cref="JsonDocument" /> it originated from.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument
            .Parse("""{ "displayName": "Living Room TV (renamed)", "isFavorite": true }""")
            .RootElement.Clone();
}
