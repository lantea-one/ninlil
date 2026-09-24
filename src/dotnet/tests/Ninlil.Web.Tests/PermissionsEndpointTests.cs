using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the <c>GET /api/permissions</c> endpoint end to end, against
///     a real, in-process host (see <see cref="NinlilDnsWebApplicationFactory" />) --
///     confirming the reflection-computed permission set actually contains a real, spot-checked
///     permission string, and that the endpoint requires an authenticated caller (any one, not a
///     specific permission -- see <see cref="Controllers.PermissionsController" />'s own doc
///     comment for why).
/// </summary>
[Collection(WebTestCollection.Name)]
public class PermissionsEndpointTests(NinlilDnsWebApplicationFactory factory)
{
    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(
                NinlilDnsWebApplicationFactory.AdminUserName,
                NinlilDnsWebApplicationFactory.AdminPassword
            )
        );
        loginResponse.EnsureSuccessStatusCode();

        LoginResponse login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            login.AccessToken
        );

        return client;
    }

    [Fact]
    public async Task List_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/permissions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_WithAnAuthenticatedCaller_ReturnsTheRealKnownPermissionSet()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync("/api/permissions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        List<string> permissions = await response.Content.ReadFromJsonAsync<List<string>>();

        // Spot-check a handful of real, known permission strings this project's own controllers
        // actually declare via [RequirePermission(...)] -- confirming this is genuinely computed
        // from the real attribute usages, not an empty or hardcoded stub list.
        Assert.Contains("zones:read", permissions);
        Assert.Contains("zones:write", permissions);
        Assert.Contains("roles:read", permissions);
        Assert.Contains("roles:write", permissions);
        Assert.Contains("users:read", permissions);
        Assert.Contains("users:write", permissions);
        Assert.Contains("webhooks:write", permissions);

        // ServicesController's own permission pair -- services:manage is this project's one
        // deliberate exception to the otherwise-universal resource:read/resource:write shape (see
        // that controller's own doc comment for the full reasoning): a distinct verb, apart from
        // every other resource's own :write, gates its genuinely dangerous start/stop/restart
        // actions specifically, so granting a role ordinary write access elsewhere never silently
        // also grants the ability to take real DNS resolution offline.
        Assert.Contains("services:read", permissions);
        Assert.Contains("services:manage", permissions);

        // Every entry is distinct and the whole list is sorted -- both real, checkable
        // properties of the reflection-computed result, not just "some entries exist".
        Assert.Equal(permissions.Distinct(StringComparer.Ordinal).Count(), permissions.Count);
        Assert.Equal(
            permissions.OrderBy(permission => permission, StringComparer.Ordinal),
            permissions
        );

        // Every permission string this project actually uses follows the "resource:action" shape
        // -- a real structural check, not just presence. "manage" is allowed alongside
        // "read"/"write" only for services:manage, ServicesController's own single, deliberate
        // exception to the otherwise-universal two-verb convention. Digits are allowed in the
        // resource segment -- "dns64settings" is the one real case, and "dns64" is the actual RFC
        // 6147 term, not an arbitrary name a stricter letters-only pattern should be forcing away
        // from.
        Assert.All(
            permissions,
            permission => Assert.Matches("^[a-z0-9]+:(read|write|manage)$", permission)
        );
    }
}
