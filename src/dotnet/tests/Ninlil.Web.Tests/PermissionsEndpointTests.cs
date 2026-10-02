using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <c>GET /api/permissions</c> over HTTP on the shared in-process host. It
///     checks that the list built by reflection from the <c>[RequirePermission]</c> attributes
///     contains known permissions, is sorted and distinct, and follows the
///     <c>resource:action</c> shape, and that the endpoint rejects a request without a token.
///     The endpoint needs only a signed-in caller, not a permission (see
///     <see cref="Controllers.PermissionsController" />), but the test signs in as the bootstrap
///     administrator, so a caller without permissions is not tried.
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

        // Spot-check permissions the controllers declare with [RequirePermission(...)], so the list
        // is not empty or a fixed stub.
        Assert.Contains("zones:read", permissions);
        Assert.Contains("zones:write", permissions);
        Assert.Contains("roles:read", permissions);
        Assert.Contains("roles:write", permissions);
        Assert.Contains("users:read", permissions);
        Assert.Contains("users:write", permissions);
        Assert.Contains("webhooks:write", permissions);

        // ServicesController uses a separate :manage verb for start, stop and restart, so that
        // write access elsewhere never also grants the power to take DNS resolution offline (see
        // ServicesController).
        Assert.Contains("services:read", permissions);
        Assert.Contains("services:manage", permissions);

        // Every entry is distinct and the list is sorted.
        Assert.Equal(permissions.Distinct(StringComparer.Ordinal).Count(), permissions.Count);
        Assert.Equal(
            permissions.OrderBy(permission => permission, StringComparer.Ordinal),
            permissions
        );

        // Every permission has the "resource:action" shape with read, write or manage as the
        // action. Several resources use manage for dangerous actions (services, storage,
        // selfupdate, dhcpservices and others); the pattern does not restrict which. Digits are
        // allowed in the resource for "dns64settings", since DNS64 is the RFC 6147 term.
        Assert.All(
            permissions,
            permission => Assert.Matches("^[a-z0-9]+:(read|write|manage)$", permission)
        );
    }
}
