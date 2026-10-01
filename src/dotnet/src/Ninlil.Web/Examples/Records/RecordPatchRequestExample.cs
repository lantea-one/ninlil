using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Records;

/// <summary>
///     This class supplies the Swagger example for the RFC 7396 merge patch body of
///     <see cref="Controllers.RecordsController.Patch" />. The body is a <see cref="JsonElement" />
///     because a merge patch must tell an absent field, which is left as it is, from a present one;
///     no typed model can express that.
/// </summary>
public class RecordPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that takes a record out of service for maintenance, with a
    ///     note, and leaves every other field as it is: the usual reason to patch a record rather
    ///     than replace it.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument
            .Parse(
                """{ "isDisabled": true, "comments": "Temporarily disabled while the origin server is under maintenance." }"""
            )
            .RootElement.Clone();
}
