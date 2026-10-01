using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientGroups;

/// <summary>
///     This class supplies the Swagger example for the RFC 7396 merge patch body of
///     <see cref="Controllers.ClientGroupsController.Patch" />. The body is a
///     <see cref="JsonElement" /> because a merge patch must tell an absent field, which is left as
///     it is, from a present one; no typed model can express that.
/// </summary>
public class ClientGroupPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch making the group the default fallback group, which takes the
    ///     flag from whichever group held it.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument.Parse("""{ "isDefault": true }""").RootElement.Clone();
}
