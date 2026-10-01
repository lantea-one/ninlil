using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Rewrites;

/// <summary>
///     This class supplies the Swagger example for the RFC 7396 merge patch body of
///     <see cref="Controllers.DnsRewritesController.Patch" />. The body is a
///     <see cref="JsonElement" /> rather than a model class because a merge patch leaves an absent
///     field as it is, and a typed model cannot tell an absent field from a default one without a
///     "was it present" flag on every property. The other merge patch examples refer here for this
///     reason.
/// </summary>
public class DnsRewritePatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that disables a rewrite and leaves every other field as it
    ///     is. <see cref="JsonDocument.RootElement" /> is cloned so the returned element stays
    ///     valid after the <see cref="JsonDocument" /> is gone.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument.Parse("""{ "isDisabled": true }""").RootElement.Clone();
}
