using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpTagRules;

/// <summary>
///     This class supplies the Swagger example for the RFC 7396 merge patch body of
///     <see cref="Controllers.DhcpTagRulesController.Patch" />. The body is a
///     <see cref="JsonElement" /> because a merge patch must tell an absent field, which is left as
///     it is, from a present one; no typed model can express that.
/// </summary>
public class DhcpTagRulePatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that changes the rule's priority from 10 to 20, so it
    ///     applies after rules with lower numbers and its options win a conflict with theirs. The
    ///     match criteria stay as they are.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument.Parse("""{ "priority": 20 }""").RootElement.Clone();
}
