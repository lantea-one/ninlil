using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.MacVendor;

/// <summary>
///     This class supplies the Swagger example for the MAC vendor settings merge patch (<c>PATCH
///     /api/mac-vendor-settings</c>): checking for registry updates every 30 days instead of every
///     14.
/// </summary>
public class MacVendorSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <inheritdoc />
    public JsonElement GetExamples() =>
        JsonDocument
            .Parse(
                """
                {
                  "refreshInterval": "30.00:00:00"
                }
                """
            )
            .RootElement.Clone();
}
