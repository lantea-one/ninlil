using System.Text.Json.Nodes;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the authentication schemes the served OpenAPI document declares
///     (<c>/swagger/v1/swagger.json</c>, fetched from the shared in-process host over real HTTP):
///     the <c>Bearer</c> JWT scheme and the <c>ApiToken</c> scheme, which Swagger UI's Authorize
///     dialog and generated clients read to learn how to send a credential.
/// </summary>
[Collection(WebTestCollection.Name)]
public class OpenApiSecuritySchemeTests(NinlilDnsWebApplicationFactory factory)
{
    /// <summary>
    ///     This method fetches and parses the served document.
    /// </summary>
    private async Task<JsonNode> GetDocumentAsync()
    {
        string served = await factory.CreateClient().GetStringAsync("/swagger/v1/swagger.json");
        return JsonNode.Parse(served);
    }

    [Fact]
    public async Task Document_Declares_TheBearerSchemeAsAnHttpBearerJwt()
    {
        JsonNode scheme = (await GetDocumentAsync())["components"]["securitySchemes"]["Bearer"];

        Assert.NotNull(scheme);
        Assert.Equal("http", scheme["type"].GetValue<string>());
        Assert.Equal("Bearer", scheme["scheme"].GetValue<string>());
    }

    [Fact]
    public async Task Document_Declares_TheApiTokenSchemeAsAnApiKeyInTheAuthorizationHeader()
    {
        JsonNode scheme = (await GetDocumentAsync())["components"]["securitySchemes"]["ApiToken"];

        Assert.NotNull(scheme);
        Assert.Equal("apiKey", scheme["type"].GetValue<string>());
        Assert.Equal("header", scheme["in"].GetValue<string>());
        Assert.Equal("Authorization", scheme["name"].GetValue<string>());

        // Swagger UI sends the typed value as written, so the description has to tell the user
        // to include the prefix the handler requires.
        Assert.Contains("ApiToken <token>", scheme["description"].GetValue<string>());
    }

    [Fact]
    public async Task Document_Requirements_OfferBearerAndApiTokenAsAlternatives()
    {
        JsonArray requirements = (await GetDocumentAsync())["security"].AsArray();

        // Two entries are alternatives (either credential works); one entry with two keys would
        // demand both at once.
        List<string> offered = [];
        foreach (JsonNode requirement in requirements)
        {
            JsonObject keys = requirement.AsObject();
            Assert.Single(keys);
            offered.Add(keys.First().Key);
        }

        Assert.Contains("Bearer", offered);
        Assert.Contains("ApiToken", offered);
    }

    [Fact]
    public async Task Document_Operations_InheritTheDocumentRequirementsWithoutOverridingThem()
    {
        JsonNode paths = (await GetDocumentAsync())["paths"];

        // The requirements are document-wide, so an operation that overrode them with its own
        // list would drop the ApiToken scheme for that operation.
        foreach ((string path, JsonNode item) in paths.AsObject())
        foreach ((string method, JsonNode operation) in item.AsObject())
        {
            if (operation is JsonObject body)
                Assert.False(
                    body.ContainsKey("security"),
                    $"{method.ToUpperInvariant()} {path} overrides the document requirements."
                );
        }
    }
}
