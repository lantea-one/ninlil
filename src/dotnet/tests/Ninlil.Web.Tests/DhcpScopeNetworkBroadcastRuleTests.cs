using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the rule that an IPv4 scope's range must not include the network or the
///     broadcast address of its subnet, on the shared host, signed in as the bootstrap
///     administrator. It checks create, replace and patch, the 400 messages, that /31 and /32
///     scopes are exempt (RFC 3021 Section 2.1) and that IPv6 is unchanged. Each test takes a /24 from <see cref="DhcpTestScopeRanges" /> so it never meets
///     another test's scope.
/// </summary>
[Collection(WebTestCollection.Name)]
public class DhcpScopeNetworkBroadcastRuleTests(NinlilDnsWebApplicationFactory factory)
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

    private static object Body(
        string protocolVersion,
        string start,
        string end,
        int prefixLength,
        string name = null
    ) =>
        new
        {
            name = name ?? $"net-bcast-{Guid.NewGuid():N}",
            protocolVersion,
            rangeStartAddress = start,
            rangeEndAddress = end,
            subnetPrefixLength = prefixLength,
            defaultLeaseDuration = "1.00:00:00",
            maxLeaseDuration = "7.00:00:00",
        };

    private static string Base24(string start) => start[..start.LastIndexOf('.')];

    [Fact]
    public async Task Create_WithARangeStartingOnTheNetworkAddress_Returns400NamingIt()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        (string start, _, int prefixLength) = DhcpTestScopeRanges.NextIPv4();
        string baseAddress = Base24(start);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            Body("IPv4", $"{baseAddress}.0", $"{baseAddress}.50", prefixLength)
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string message = await response.Content.ReadAsStringAsync();
        Assert.Contains("network address", message);
        Assert.Contains($"{baseAddress}.0", message);
    }

    [Fact]
    public async Task Create_WithARangeEndingOnTheBroadcastAddress_Returns400NamingIt()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        (string start, _, int prefixLength) = DhcpTestScopeRanges.NextIPv4();
        string baseAddress = Base24(start);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            Body("IPv4", $"{baseAddress}.100", $"{baseAddress}.255", prefixLength)
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string message = await response.Content.ReadAsStringAsync();
        Assert.Contains("broadcast address", message);
        Assert.Contains($"{baseAddress}.255", message);
    }

    [Fact]
    public async Task Create_WithARangeThatStopsJustInsideTheSubnet_IsAccepted()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        (string start, _, int prefixLength) = DhcpTestScopeRanges.NextIPv4();
        string baseAddress = Base24(start);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            Body("IPv4", $"{baseAddress}.1", $"{baseAddress}.254", prefixLength)
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithARangeOnTheEdgesOfASmallerSubnet_JudgesAgainstThatSubnet()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        (string start, _, _) = DhcpTestScopeRanges.NextIPv4();
        string baseAddress = Base24(start);

        // In the upper /25 of a /24 the network address is .128; in the lower /25 the broadcast
        // address is .127. .1 to .126 is the lower /25 without its edges.
        HttpResponseMessage upperNetwork = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            Body("IPv4", $"{baseAddress}.128", $"{baseAddress}.200", 25)
        );
        HttpResponseMessage lowerBroadcast = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            Body("IPv4", $"{baseAddress}.100", $"{baseAddress}.127", 25)
        );
        HttpResponseMessage fits = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            Body("IPv4", $"{baseAddress}.1", $"{baseAddress}.126", 25)
        );

        Assert.Equal(HttpStatusCode.BadRequest, upperNetwork.StatusCode);
        Assert.Contains("network address", await upperNetwork.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, lowerBroadcast.StatusCode);
        Assert.Contains("broadcast address", await lowerBroadcast.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Created, fits.StatusCode);
    }

    [Fact]
    public async Task Create_WithAWholeSlash31_IsAcceptedBecauseItHasNoNetworkOrBroadcastAddress()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        (string start, _, _) = DhcpTestScopeRanges.NextIPv4();
        string baseAddress = Base24(start);

        // RFC 3021 Section 2.1: both addresses of a 31-bit prefix are host addresses.
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            Body("IPv4", $"{baseAddress}.0", $"{baseAddress}.1", 31)
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithASingleAddressSlash32_IsAccepted()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        (string start, _, _) = DhcpTestScopeRanges.NextIPv4();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            Body("IPv4", start, start, 32)
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithASlash30RangeOfBothHostAddresses_IsAcceptedAndWithAllFourIsNot()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        (string start, _, _) = DhcpTestScopeRanges.NextIPv4();
        string baseAddress = Base24(start);

        HttpResponseMessage hosts = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            Body("IPv4", $"{baseAddress}.5", $"{baseAddress}.6", 30)
        );
        HttpResponseMessage all = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            Body("IPv4", $"{baseAddress}.8", $"{baseAddress}.11", 30)
        );

        Assert.Equal(HttpStatusCode.Created, hosts.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, all.StatusCode);
    }

    [Fact]
    public async Task Create_WithAnIPv6RangeUpToTheLastAddressOfItsSubnet_IsStillAccepted()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        (string start, _, int prefixLength) = DhcpTestScopeRanges.NextIPv6();
        string head = start[..start.IndexOf("::")];

        // DHCPv6 has no broadcast address, so the first and last address of the /64 may be used.
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            Body("IPv6", $"{head}::", $"{head}:0:ffff:ffff:ffff:ffff", prefixLength)
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task ReplaceAndPatch_ToARangeThatIncludesTheNetworkOrBroadcastAddress_Return400AndLeaveTheScope()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        (string start, _, int prefixLength) = DhcpTestScopeRanges.NextIPv4();
        string baseAddress = Base24(start);
        HttpResponseMessage created = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            Body("IPv4", $"{baseAddress}.10", $"{baseAddress}.50", prefixLength)
        );
        DhcpScopeDetail scope = await created.Content.ReadFromJsonAsync<DhcpScopeDetail>();

        HttpResponseMessage replace = await client.PutAsJsonAsync(
            $"/api/dhcp-scopes/{scope.Id}",
            Body("IPv4", $"{baseAddress}.0", $"{baseAddress}.50", prefixLength, scope.Name)
        );
        HttpResponseMessage patchEnd = await client.PatchAsJsonAsync(
            $"/api/dhcp-scopes/{scope.Id}",
            new { rangeEndAddress = $"{baseAddress}.255" }
        );
        // A wider mask turns an address that was a host address into a broadcast address.
        HttpResponseMessage patchPrefix = await client.PatchAsJsonAsync(
            $"/api/dhcp-scopes/{scope.Id}",
            new { rangeEndAddress = $"{baseAddress}.63", subnetPrefixLength = 26 }
        );

        Assert.Equal(HttpStatusCode.BadRequest, replace.StatusCode);
        Assert.Contains("network address", await replace.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, patchEnd.StatusCode);
        Assert.Contains("broadcast address", await patchEnd.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, patchPrefix.StatusCode);

        DhcpScopeDetail after = await (
            await client.GetAsync($"/api/dhcp-scopes/{scope.Id}")
        ).Content.ReadFromJsonAsync<DhcpScopeDetail>();
        Assert.Equal($"{baseAddress}.10", after.RangeStartAddress);
        Assert.Equal($"{baseAddress}.50", after.RangeEndAddress);
    }
}
