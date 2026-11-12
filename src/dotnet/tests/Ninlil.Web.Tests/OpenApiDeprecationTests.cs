using System.Text.Json.Nodes;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the deprecation marks in the generated OpenAPI document, on the shared
///     in-process host with real HTTP. The identity settings model keeps three lockout properties
///     for compatibility; the document must flag them deprecated and must still list them.
/// </summary>
[Collection(WebTestCollection.Name)]
public class OpenApiDeprecationTests(NinlilDnsWebApplicationFactory factory)
{
    [Theory]
    [InlineData("defaultLockoutTimeSpan")]
    [InlineData("maxFailedAccessAttempts")]
    [InlineData("allowedForNewUsers")]
    public async Task Document_IdentitySettingsLockoutProperty_IsDeprecatedAndStillListed(
        string propertyName
    )
    {
        string served = await factory.CreateClient().GetStringAsync("/swagger/v1/swagger.json");
        JsonNode document = JsonNode.Parse(served);

        JsonNode property = document["components"]["schemas"]["IdentitySettingsDetail"][
            "properties"
        ][propertyName];

        Assert.NotNull(property);
        Assert.True(property["deprecated"]?.GetValue<bool>() ?? false);
    }

    [Fact]
    public async Task Document_IdentitySettingsPasswordProperty_IsNotDeprecated()
    {
        string served = await factory.CreateClient().GetStringAsync("/swagger/v1/swagger.json");
        JsonNode document = JsonNode.Parse(served);

        JsonNode property = document["components"]["schemas"]["IdentitySettingsDetail"][
            "properties"
        ]["requiredLength"];

        Assert.NotNull(property);
        Assert.Null(property["deprecated"]);
    }
}
