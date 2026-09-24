using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpScopes;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.DhcpScopesController.Patch" />'s own RFC 7396 JSON Merge Patch
///     request body. The request parameter's own declared type is the generic
///     <see cref="JsonElement" />, not a dedicated model class -- a field this project's merge
///     patch endpoints leave out of the request body entirely is left untouched, which no
///     strongly-typed C# model can represent.
/// </summary>
public class DhcpScopePatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch narrowing this scope's own address range and disabling it,
    ///     leaving every other field -- lease durations, gateway/DNS server addresses, and so on --
    ///     exactly as it was.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument
            .Parse(
                """
                { "rangeStartAddress": "192.168.1.150", "isDisabled": true }
                """
            )
            .RootElement.Clone();
}
