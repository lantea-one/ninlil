using Ninlil.Web.OpenApi;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="OperationIdConvention" />, the rule that names every operation
///     in the published OpenAPI document, with plain unit tests (no host). The admin console's
///     generated client names its methods after these ids, so each case pins the id of a route
///     shape that occurs in the API: a plain path, braces, hyphens, nested ids and a route
///     constraint.
/// </summary>
public class OperationIdConventionTests
{
    [Theory]
    [InlineData("documentation", "GET", "documentationGet")]
    [InlineData("api/zones", "GET", "apiZonesGet")]
    [InlineData("api/zones", "POST", "apiZonesPost")]
    [InlineData("api/zones/{id}", "DELETE", "apiZonesIdDelete")]
    [InlineData("api/zones/{id}/catalog-members", "GET", "apiZonesIdCatalogMembersGet")]
    [InlineData(
        "api/clients/{clientId}/schedules/{scheduleId}",
        "PUT",
        "apiClientsClientIdSchedulesScheduleIdPut"
    )]
    [InlineData("api/dns-rewrites/{id:guid}", "PATCH", "apiDnsRewritesIdPatch")]
    [InlineData("api/some_thing.v2/{name?}", "GET", "apiSomeThingV2NameGet")]
    public void Build_ForARouteAndMethod_ReturnsThePathInCamelCaseThenTheMethod(
        string relativePath,
        string httpMethod,
        string expected
    )
    {
        Assert.Equal(expected, OperationIdConvention.Build(relativePath, httpMethod));
    }

    [Fact]
    public void Build_ForTheSamePathWithDifferentMethods_ReturnsDifferentIds()
    {
        Assert.NotEqual(
            OperationIdConvention.Build("api/zones/{id}", "GET"),
            OperationIdConvention.Build("api/zones/{id}", "PUT")
        );
    }
}
