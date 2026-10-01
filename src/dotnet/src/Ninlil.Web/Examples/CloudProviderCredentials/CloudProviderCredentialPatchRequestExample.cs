using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.CloudProviderCredentials;

/// <summary>
///     This class supplies the Swagger example for the RFC 7396 merge patch body of
///     <see cref="Controllers.CloudProviderCredentialsController.Patch" />. The body is a
///     <see cref="JsonElement" /> because a merge patch must tell an absent field, which is left as
///     it is, from a present one; no typed model can express that.
/// </summary>
public class CloudProviderCredentialPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that changes only the comments and leaves out
    ///     <c>credentials</c>, so the stored secrets stay as they are. Including <c>credentials</c>
    ///     replaces them (see <see cref="Controllers.CloudProviderCredentialsController.Patch" />).
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument
            .Parse("""{ "comments": "Reviewed and confirmed still in active use, 2026-08-31." }""")
            .RootElement.Clone();
}
