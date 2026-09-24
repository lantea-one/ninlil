using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ninlil.Dhcp.Common.OptionCatalog;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="DhcpOptionCatalogController" />'s own
///     <c>GET /api/dhcp-option-catalog</c> REST endpoint end to end, against a real, in-process
///     host (see <see cref="NinlilDnsWebApplicationFactory" />) -- proving this REST surface
///     genuinely returns this project's own real, compiled-in <see cref="Dhcpv4OptionCatalog" />/
///     <see cref="Dhcpv6OptionCatalog" /> contents, correctly mapped and correctly filtered by the
///     requested protocol version, rather than a mock or a separate copy.
/// </summary>
[Collection(WebTestCollection.Name)]
public class DhcpOptionCatalogControllerTests(NinlilDnsWebApplicationFactory factory)
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
    public async Task List_WithProtocolVersionIPv4_ReturnsTheRealDhcpv4Catalog()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync(
            "/api/dhcp-option-catalog?protocolVersion=IPv4"
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        List<DhcpOptionCatalogEntryDetail> entries = await response.Content.ReadFromJsonAsync<
            List<DhcpOptionCatalogEntryDetail>
        >();

        Assert.Equal(Dhcpv4OptionCatalog.Entries.Count, entries.Count);
        Assert.Contains(entries, entry => entry.Code == 1 && entry.Name == "Subnet Mask");
        Assert.Contains(
            entries,
            entry => entry.Code == 6 && entry.ValueType == DhcpOptionValueTypeEnum.IPv4AddressList
        );

        // Every code returned is unique, and every entry carries a non-empty name/description --
        // proving the real mapping, not just a count match.
        Assert.Equal(entries.Select(entry => entry.Code).Distinct().Count(), entries.Count);
        Assert.All(
            entries,
            entry =>
            {
                Assert.False(string.IsNullOrWhiteSpace(entry.Name));
                Assert.False(string.IsNullOrWhiteSpace(entry.Description));
            }
        );
    }

    [Fact]
    public async Task List_WithProtocolVersionIPv6_ReturnsTheRealDhcpv6Catalog()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync(
            "/api/dhcp-option-catalog?protocolVersion=IPv6"
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        List<DhcpOptionCatalogEntryDetail> entries = await response.Content.ReadFromJsonAsync<
            List<DhcpOptionCatalogEntryDetail>
        >();

        Assert.Equal(Dhcpv6OptionCatalog.Entries.Count, entries.Count);
        Assert.Contains(
            entries,
            entry => entry.Code == 23 && entry.Name == "DNS Recursive Name Server"
        );

        // The IPv4 and IPv6 responses are genuinely different -- confirms the protocol filter is
        // real, not a coincidence of both catalogs happening to be the same size.
        Assert.NotEqual(Dhcpv4OptionCatalog.Entries.Count, entries.Count);
    }

    [Fact]
    public async Task List_ServerAddressEligibleEntries_AreAlwaysAddressTyped()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        foreach (string protocolVersion in new[] { "IPv4", "IPv6" })
        {
            HttpResponseMessage response = await client.GetAsync(
                $"/api/dhcp-option-catalog?protocolVersion={protocolVersion}"
            );
            response.EnsureSuccessStatusCode();

            List<DhcpOptionCatalogEntryDetail> entries = await response.Content.ReadFromJsonAsync<
                List<DhcpOptionCatalogEntryDetail>
            >();

            DhcpOptionValueTypeEnum[] addressTypes =
            [
                DhcpOptionValueTypeEnum.IPv4Address,
                DhcpOptionValueTypeEnum.IPv4AddressList,
                DhcpOptionValueTypeEnum.IPv6Address,
                DhcpOptionValueTypeEnum.IPv6AddressList,
            ];

            Assert.All(
                entries.Where(entry => entry.IsServerAddressEligible),
                entry => Assert.Contains(entry.ValueType, addressTypes)
            );
        }
    }

    [Fact]
    public async Task List_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(
            "/api/dhcp-option-catalog?protocolVersion=IPv4"
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_WithoutAProtocolVersion_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync("/api/dhcp-option-catalog");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_WithAnUnrecognizedProtocolVersion_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync(
            "/api/dhcp-option-catalog?protocolVersion=this-is-not-a-real-protocol"
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
