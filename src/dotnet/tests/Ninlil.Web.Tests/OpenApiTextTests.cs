using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class guards the text of the served OpenAPI document (<c>/swagger/v1/swagger.json</c>,
///     fetched from the shared in-process host): no summary or description may hold an internal XML
///     documentation ID or a namespace-qualified Ninlil name. Swashbuckle copies cref references
///     verbatim, and <c>XmlCrefTextDocumentFilter</c> rewrites them; this test fails when a new
///     cref in <c>Ninlil.Web</c> text slips past the filter.
/// </summary>
[Collection(WebTestCollection.Name)]
public partial class OpenApiTextTests(NinlilDnsWebApplicationFactory factory)
{
    /// <summary>
    ///     This pattern matches a documentation ID (<c>M:Ninlil.</c>, <c>T:Ninlil.</c> and so on)
    ///     or a name qualified with a Ninlil namespace root.
    /// </summary>
    [GeneratedRegex(@"\b[MTPFE]:Ninlil\.|\bNinlil\.(Web|Dns|Dhcp|Common)\.[A-Z]")]
    private static partial Regex InternalNamePattern();

    [Fact]
    public async Task ServedDocument_HoldsNoDocumentationIdOrQualifiedNinlilNameInItsText()
    {
        string served = await factory.CreateClient().GetStringAsync("/swagger/v1/swagger.json");
        List<string> offenders = [];

        Walk(JsonNode.Parse(served), "$", offenders);

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} summaries or descriptions hold an internal name. First ones:\n"
                + string.Join("\n", offenders.Take(10))
        );
    }

    /// <summary>
    ///     This method visits every JSON value and records the path and text of each "summary" or
    ///     "description" string that matches the pattern. Property names that happen to be called
    ///     "description" inside a schema's <c>properties</c> hold a schema object, not a string,
    ///     so only string values are tested.
    /// </summary>
    private static void Walk(JsonNode node, string path, List<string> offenders)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (KeyValuePair<string, JsonNode> property in obj)
                {
                    string childPath = path + "/" + property.Key;
                    if (
                        property.Key is "summary" or "description"
                        && property.Value is JsonValue value
                        && value.TryGetValue(out string text)
                        && InternalNamePattern().IsMatch(text)
                    )
                        offenders.Add($"{childPath}: {text}");
                    else
                        Walk(property.Value, childPath, offenders);
                }
                break;
            case JsonArray array:
                for (int index = 0; index < array.Count; index++)
                    Walk(array[index], $"{path}/{index}", offenders);
                break;
        }
    }
}
