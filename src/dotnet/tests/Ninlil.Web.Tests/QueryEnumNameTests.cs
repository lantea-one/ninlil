using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers how enum query-string parameters bind, over HTTP on the shared host and
///     signed in as the bootstrap administrator: <c>presence</c> and <c>sort</c> of
///     <see cref="ClientsController" /> and <c>protocolVersion</c> of
///     <see cref="DhcpOptionCatalogController" />. Each takes declared names only, in any case and
///     with surrounding whitespace; a digit string or an undefined name gives 400 with the valid
///     names, and an omitted optional parameter behaves as before. The tests assert on status codes
///     and the error text, not on row counts, because other tests share the data.
/// </summary>
[Collection(WebTestCollection.Name)]
public class QueryEnumNameTests(NinlilDnsWebApplicationFactory factory)
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

    /// <summary>
    ///     Each case is a parameter, a value that must be refused and the names the message lists.
    ///     The digit values are defined members' numbers, which MVC's own enum binder accepts.
    /// </summary>
    public static TheoryData<string, string, string> RefusedValues =>
        new()
        {
            { "/api/clients", "presence", "0" },
            { "/api/clients", "presence", "1" },
            { "/api/clients", "presence", "7" },
            { "/api/clients", "presence", "Sideways" },
            { "/api/clients", "sort", "0" },
            { "/api/clients", "sort", "2" },
            { "/api/clients", "sort", "99" },
            { "/api/clients", "sort", "Newest" },
            { "/api/dhcp-option-catalog", "protocolVersion", "4" },
            { "/api/dhcp-option-catalog", "protocolVersion", "6" },
            { "/api/dhcp-option-catalog", "protocolVersion", "5" },
            { "/api/dhcp-option-catalog", "protocolVersion", "IPv5" },
        };

    [Theory]
    [MemberData(nameof(RefusedValues))]
    public async Task Get_WithADigitStringOrAnUndefinedName_ReturnsBadRequestListingTheNames(
        string path,
        string parameter,
        string value
    )
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string separator = path.Contains('?') ? "&" : "?";

        HttpResponseMessage response = await client.GetAsync(
            $"{path}{separator}{parameter}={Uri.EscapeDataString(value)}"
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        string[] expectedNames = parameter switch
        {
            "presence" => ["Online", "Offline"],
            "sort" => ["Created", "LastSeen", "DisplayName"],
            _ => ["IPv4", "IPv6"],
        };
        Assert.All(expectedNames, name => Assert.Contains(name, body));
    }

    [Theory]
    [InlineData("presence", "Online")]
    [InlineData("presence", "online")]
    [InlineData("presence", "OFFLINE")]
    [InlineData("presence", " Offline ")]
    [InlineData("sort", "Created")]
    [InlineData("sort", "lastseen")]
    [InlineData("sort", "DISPLAYNAME")]
    [InlineData("sort", " LastSeen ")]
    public async Task ClientList_WithAValidName_IsAcceptedInAnyCase(string parameter, string value)
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync(
            $"/api/clients?{parameter}={Uri.EscapeDataString(value)}"
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("IPv4")]
    [InlineData("ipv6")]
    [InlineData("IPV4")]
    [InlineData(" IPv6 ")]
    public async Task OptionCatalog_WithAValidName_IsAcceptedInAnyCase(string value)
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync(
            $"/api/dhcp-option-catalog?protocolVersion={Uri.EscapeDataString(value)}"
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/clients")]
    [InlineData("/api/clients?presence=&sort=")]
    public async Task ClientList_WithTheOptionalParametersOmittedOrBlank_ReturnsTheList(string url)
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OptionCatalog_WithTheParameterBlank_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync(
            "/api/dhcp-option-catalog?protocolVersion="
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
