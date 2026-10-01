using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Zones;

/// <summary>
///     This class supplies the Swagger example for the RFC 7396 merge patch body of
///     <see cref="Controllers.ZonesController.Patch" />. The body is a <see cref="JsonElement" />
///     because a merge patch must tell an absent field, which is left as it is, from a present one;
///     no typed model can express that.
/// </summary>
public class ZonePatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that sets <c>autoManageCaaRecords</c> to JSON <c>null</c>,
    ///     so the zone goes back to inheriting the server-wide CAA default; a nullable field is
    ///     cleared this way.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument.Parse("""{ "autoManageCaaRecords": null }""").RootElement.Clone();
}
