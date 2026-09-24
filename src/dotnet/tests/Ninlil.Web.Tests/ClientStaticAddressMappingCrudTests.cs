using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the static address mapping CRUD REST endpoints
///     (<see cref="DnsClientStaticAddressMappingsController" />) end to end, against a real,
///     in-process host and a real SQLite database (see <see cref="NinlilDnsWebApplicationFactory" />)
///     -- a full create/list/get/PUT/delete lifecycle, CIDR/protocol-family-agreement validation,
///     and the unauthenticated-request rejection every other controller already gets covered for.
/// </summary>
[Collection(WebTestCollection.Name)]
public class ClientStaticAddressMappingCrudTests(NinlilDnsWebApplicationFactory factory)
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
    public async Task MappingLifecycle_CreateListGetReplaceAndDelete_WorksEndToEnd()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string identifier = $"aa:bb:cc:dd:{Guid.NewGuid():N}"[..17];

        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/client-static-address-mappings",
            new
            {
                cidr = "203.0.113.42/32",
                clientIdentifier = identifier,
                protocolVersion = "IPv4",
                comments = "Guest VLAN printer",
            }
        );
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        DnsClientStaticAddressMappingDetail created =
            await createResponse.Content.ReadFromJsonAsync<DnsClientStaticAddressMappingDetail>();
        Assert.Equal("203.0.113.42/32", created.Cidr);
        Assert.Equal(identifier, created.ClientIdentifier);
        Assert.False(created.IsDisabled);
        Assert.Equal("Guest VLAN printer", created.Comments);

        // Listing reflects it, with pagination fields present.
        HttpResponseMessage listResponse = await client.GetAsync(
            "/api/client-static-address-mappings"
        );
        listResponse.EnsureSuccessStatusCode();
        PagedResult<DnsClientStaticAddressMappingDetail> page =
            await listResponse.Content.ReadFromJsonAsync<
                PagedResult<DnsClientStaticAddressMappingDetail>
            >();
        Assert.Contains(page.Items, item => item.Id == created.Id);
        Assert.True(page.Take > 0);

        // Getting it by identifier.
        HttpResponseMessage getResponse = await client.GetAsync(
            $"/api/client-static-address-mappings/{created.Id}"
        );
        getResponse.EnsureSuccessStatusCode();
        DnsClientStaticAddressMappingDetail fetched =
            await getResponse.Content.ReadFromJsonAsync<DnsClientStaticAddressMappingDetail>();
        Assert.Equal(created.Id, fetched.Id);

        // PUT replaces the CIDR, keeping the identifier and disabling it.
        HttpResponseMessage replaceResponse = await client.PutAsJsonAsync(
            $"/api/client-static-address-mappings/{created.Id}",
            new
            {
                cidr = "203.0.113.0/24",
                clientIdentifier = identifier,
                protocolVersion = "IPv4",
                isDisabled = true,
            }
        );
        Assert.Equal(HttpStatusCode.NoContent, replaceResponse.StatusCode);

        HttpResponseMessage afterReplaceResponse = await client.GetAsync(
            $"/api/client-static-address-mappings/{created.Id}"
        );
        DnsClientStaticAddressMappingDetail replaced =
            await afterReplaceResponse.Content.ReadFromJsonAsync<DnsClientStaticAddressMappingDetail>();
        Assert.Equal(created.Id, replaced.Id);
        Assert.Equal("203.0.113.0/24", replaced.Cidr);
        Assert.True(replaced.IsDisabled);

        // Delete it.
        HttpResponseMessage deleteResponse = await client.DeleteAsync(
            $"/api/client-static-address-mappings/{created.Id}"
        );
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        HttpResponseMessage afterDeleteResponse = await client.GetAsync(
            $"/api/client-static-address-mappings/{created.Id}"
        );
        Assert.Equal(HttpStatusCode.NotFound, afterDeleteResponse.StatusCode);
    }

    [Fact]
    public async Task Create_WithAnInvalidCidr_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/client-static-address-mappings",
            new
            {
                cidr = "not a cidr at all",
                clientIdentifier = "aa:bb:cc:dd:ee:ff",
                protocolVersion = "IPv4",
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithMismatchedProtocolVersionAndAddressFamily_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        // An IPv4 network paired with a declared IPv6 protocol version -- the two must agree, per
        // DnsClientStaticAddressMappingEntity.ProtocolVersion's own doc comment.
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/client-static-address-mappings",
            new
            {
                cidr = "203.0.113.0/24",
                clientIdentifier = "00:01:00:01:aa:bb:cc:dd:ee:ff",
                protocolVersion = "IPv6",
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithNoClientIdentifier_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/client-static-address-mappings",
            new
            {
                cidr = "203.0.113.0/24",
                clientIdentifier = "",
                protocolVersion = "IPv4",
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithABareAddressRatherThanACidr_IsAccepted()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/client-static-address-mappings",
            new
            {
                cidr = $"198.51.100.{Random.Shared.Next(2, 254)}",
                clientIdentifier = $"aa:bb:cc:dd:ee:{Random.Shared.Next(10, 99)}",
                protocolVersion = "IPv4",
            }
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task ClientStaticAddressMappingsEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/client-static-address-mappings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
