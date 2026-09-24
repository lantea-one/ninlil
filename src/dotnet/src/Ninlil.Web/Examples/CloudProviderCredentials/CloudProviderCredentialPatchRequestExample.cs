using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.CloudProviderCredentials;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.CloudProviderCredentialsController.Patch" />'s own RFC 7396 JSON
///     Merge Patch request body. The request parameter's own declared type is the generic
///     <see cref="JsonElement" />, not a dedicated model class -- a field this project's merge
///     patch endpoints leave out of the request body entirely is left untouched, which no
///     strongly-typed C# model can represent.
/// </summary>
public class CloudProviderCredentialPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a metadata-only patch that omits <c>credentials</c> entirely,
    ///     leaving the stored secret material completely untouched -- see
    ///     <see cref="Controllers.CloudProviderCredentialsController.Patch" />'s own doc comment
    ///     for the alternative shape (including <c>credentials</c> to rotate it atomically).
    ///     <see cref="JsonDocument.RootElement" /> is cloned so the returned
    ///     <see cref="JsonElement" /> stays valid independently of the parsed
    ///     <see cref="JsonDocument" /> it originated from.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument
            .Parse("""{ "comments": "Reviewed and confirmed still in active use, 2026-08-31." }""")
            .RootElement.Clone();
}
