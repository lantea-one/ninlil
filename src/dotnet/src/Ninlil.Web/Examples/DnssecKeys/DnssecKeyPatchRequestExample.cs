using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DnssecKeys;

/// <summary>
///     This class supplies the Swagger example for the RFC 7396 merge patch body of
///     <see cref="Controllers.DnssecKeysController.Patch" />. The body is a
///     <see cref="JsonElement" /> because a merge patch must tell an absent field, which is left as
///     it is, from a present one; no typed model can express that.
/// </summary>
public class DnssecKeyPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that retires a key (moves it to <c>Removed</c>) and leaves
    ///     its comments as they are.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument.Parse("""{ "isRetired": true }""").RootElement.Clone();
}
