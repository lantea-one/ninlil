using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.TsigKeys;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.TsigKeysController.Patch" />'s own RFC 7396 JSON Merge Patch request
///     body. The request parameter's own declared type is the generic <see cref="JsonElement" />,
///     not a dedicated model class -- a field this project's merge patch endpoints leave out of
///     the request body entirely is left untouched, which no strongly-typed C# model can
///     represent.
/// </summary>
public class TsigKeyPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch disabling a key without touching any of its other fields --
    ///     <see cref="JsonDocument.RootElement" /> is cloned so the returned
    ///     <see cref="JsonElement" /> stays valid independently of the parsed
    ///     <see cref="JsonDocument" /> it originated from.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument.Parse("""{ "isDisabled": true }""").RootElement.Clone();
}
