using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the address family rule of the reservations API on the shared host,
///     signed in as the bootstrap administrator: a reservation in a DHCPv4 scope must hold an
///     IPv4 address and one in a DHCPv6 scope an IPv6 address, for create and for replace, and the
///     400 message names both. A stored reservation of the wrong family makes the engine unable
///     to build the reply, so the API refuses it up front.
/// </summary>
[Collection(WebTestCollection.Name)]
public class DhcpReservationAddressFamilyTests(NinlilDnsWebApplicationFactory factory)
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

    private static async Task<DhcpScopeDetail> CreateScopeAsync(
        HttpClient client,
        string protocolVersion
    )
    {
        (string start, string end, int prefixLength) =
            protocolVersion == "IPv6"
                ? DhcpTestScopeRanges.NextIPv6()
                : DhcpTestScopeRanges.NextIPv4();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            new
            {
                name = $"family-{Guid.NewGuid():N}",
                protocolVersion,
                rangeStartAddress = start,
                rangeEndAddress = end,
                subnetPrefixLength = prefixLength,
                defaultLeaseDuration = "1.00:00:00",
                maxLeaseDuration = "7.00:00:00",
            }
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<DhcpScopeDetail>();
    }

    [Fact]
    public async Task Create_AnIPv6AddressInADhcpv4Scope_Returns400NamingBothFamilies()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        DhcpScopeDetail scope = await CreateScopeAsync(client, "IPv4");

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/dhcp-scopes/{scope.Id}/reservations",
            new { clientIdentifier = "aa:bb:cc:00:01:01", ipAddress = "2001:db8::10" }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("is an IPv6 address", body);
        Assert.Contains("needs an IPv4 address", body);
    }

    [Fact]
    public async Task Create_AnIPv4AddressInADhcpv6Scope_Returns400NamingBothFamilies()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        DhcpScopeDetail scope = await CreateScopeAsync(client, "IPv6");

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/dhcp-scopes/{scope.Id}/reservations",
            new { clientIdentifier = "00:03:00:01:aa:bb:cc:00:01:02", ipAddress = "192.0.2.10" }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("is an IPv4 address", body);
        Assert.Contains("needs an IPv6 address", body);
    }

    [Fact]
    public async Task Replace_AnAddressOfTheOtherFamily_Returns400AndKeepsTheReservation()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        DhcpScopeDetail scope = await CreateScopeAsync(client, "IPv4");
        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            $"/api/dhcp-scopes/{scope.Id}/reservations",
            new { clientIdentifier = "aa:bb:cc:00:01:03", ipAddress = scope.RangeStartAddress }
        );
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        DhcpReservationDetail created =
            await createResponse.Content.ReadFromJsonAsync<DhcpReservationDetail>();

        HttpResponseMessage replaceResponse = await client.PutAsJsonAsync(
            $"/api/dhcp-scopes/{scope.Id}/reservations/{created.Id}",
            new { clientIdentifier = "aa:bb:cc:00:01:03", ipAddress = "2001:db8::11" }
        );

        Assert.Equal(HttpStatusCode.BadRequest, replaceResponse.StatusCode);
        DhcpReservationDetail after = await (
            await client.GetAsync($"/api/dhcp-scopes/{scope.Id}/reservations/{created.Id}")
        ).Content.ReadFromJsonAsync<DhcpReservationDetail>();
        Assert.Equal(scope.RangeStartAddress, after.IpAddress);
    }

    [Fact]
    public async Task Create_AnAddressOfTheScopesFamily_IsAccepted()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        DhcpScopeDetail v4 = await CreateScopeAsync(client, "IPv4");
        DhcpScopeDetail v6 = await CreateScopeAsync(client, "IPv6");

        HttpResponseMessage v4Response = await client.PostAsJsonAsync(
            $"/api/dhcp-scopes/{v4.Id}/reservations",
            new { clientIdentifier = "aa:bb:cc:00:01:04", ipAddress = v4.RangeStartAddress }
        );
        HttpResponseMessage v6Response = await client.PostAsJsonAsync(
            $"/api/dhcp-scopes/{v6.Id}/reservations",
            new
            {
                clientIdentifier = "00:03:00:01:aa:bb:cc:00:01:05",
                ipAddress = v6.RangeStartAddress,
            }
        );

        Assert.Equal(HttpStatusCode.Created, v4Response.StatusCode);
        Assert.Equal(HttpStatusCode.Created, v6Response.StatusCode);
    }
}
