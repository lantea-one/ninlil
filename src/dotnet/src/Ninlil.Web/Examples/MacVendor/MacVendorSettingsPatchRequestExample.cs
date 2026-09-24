using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.MacVendor;

/// <summary>
///     This class provides the Swagger example for a MAC vendor settings merge patch --
///     checking for registry updates monthly instead of fortnightly.
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
