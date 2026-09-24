using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the device icon, favorite star, and read-time MAC vendor fields
///     <see cref="ClientsController" /> carries: create/replace/patch round trips, strict
///     icon-name validation, the pushed-down favorites filter, a real vendor resolved from the
///     embedded IEEE snapshot (for a plain MAC and a DUID-LL alike), a vendor-based icon suggestion
///     reported alongside rather than instead of an explicit choice, and a randomized MAC reported
///     as such instead of as an unknown vendor.
/// </summary>
[Collection(WebTestCollection.Name)]
public class ClientDeviceIconFavoriteVendorTests(NinlilDnsWebApplicationFactory factory)
{
    /// <summary>
    ///     This method creates an HTTP client signed in as the bootstrap administrator.
    /// </summary>
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
    ///     This method builds a JSON merge-patch request.
    /// </summary>
    private static HttpRequestMessage PatchRequest(string url, object body) =>
        new(HttpMethod.Patch, url)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json"
            ),
        };

    /// <summary>
    ///     This method returns three random hexadecimal octets, colon-separated, so every test
    ///     registers identifiers no other test in the shared host uses.
    /// </summary>
    private static string RandomSuffix()
    {
        string hex = Guid.NewGuid().ToString("N")[..6];
        return $"{hex[..2]}:{hex[2..4]}:{hex[4..]}";
    }

    [Fact]
    public async Task ARealVendorMac_ResolvesItsVendorAndSuggestsAnIconAtReadTime()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        // B8:27:EB is the Raspberry Pi Foundation's MA-L block in the embedded IEEE snapshot.
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/clients",
            new { clientIdentifier = $"b8:27:eb:{RandomSuffix()}", protocolVersion = "IPv4" }
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        ClientDetail created = await response.Content.ReadFromJsonAsync<ClientDetail>();

        Assert.Contains("Raspberry Pi", created.Vendor);
        Assert.False(created.IsRandomizedMacAddress);
        Assert.Null(created.DeviceIconKey);
        Assert.Equal(DnsClientDeviceIconEnum.SingleBoardComputer, created.SuggestedDeviceIconKey);
        ClientAddressDetail address = Assert.Single(created.Addresses);
        Assert.StartsWith("b8:27:eb:", address.MacAddress);
        Assert.Contains("Raspberry Pi", address.Vendor);

        // The nested address resource reports the same per-address vendor.
        ClientAddressDetail fetched = (
            await client.GetFromJsonAsync<PagedResult<ClientAddressDetail>>(
                $"/api/clients/{created.Id}/addresses"
            )
        ).Items.Single();
        Assert.Contains("Raspberry Pi", fetched.Vendor);
    }

    [Fact]
    public async Task ADuidLlIdentifier_ResolvesTheVendorOfItsEmbeddedMac()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        // DUID-LL (type 3, hardware type 1) wrapping Synology's 00:11:32 block.
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/clients",
            new
            {
                clientIdentifier = $"00:03:00:01:00:11:32:{RandomSuffix()}",
                protocolVersion = "IPv6",
            }
        );
        ClientDetail created = await response.Content.ReadFromJsonAsync<ClientDetail>();

        Assert.Contains("Synology", created.Vendor);
        Assert.Equal(DnsClientDeviceIconEnum.Server, created.SuggestedDeviceIconKey);
    }

    [Fact]
    public async Task ARandomizedMac_IsReportedAsRandomizedRatherThanLookedUp()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/clients",
            new { clientIdentifier = $"da:a1:19:{RandomSuffix()}", protocolVersion = "IPv4" }
        );
        ClientDetail created = await response.Content.ReadFromJsonAsync<ClientDetail>();

        Assert.Null(created.Vendor);
        Assert.True(created.IsRandomizedMacAddress);
        Assert.True(Assert.Single(created.Addresses).IsRandomizedMacAddress);
        Assert.Null(created.SuggestedDeviceIconKey);
    }

    [Fact]
    public async Task DeviceIconAndFavorite_RoundTripThroughCreateReplaceAndPatch()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/clients",
            new
            {
                clientIdentifier = $"b8:27:eb:{RandomSuffix()}",
                protocolVersion = "IPv4",
                deviceIconKey = "television",
                isFavorite = true,
            }
        );
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        ClientDetail created = await createResponse.Content.ReadFromJsonAsync<ClientDetail>();
        Assert.Equal(DnsClientDeviceIconEnum.Television, created.DeviceIconKey);
        Assert.True(created.IsFavorite);

        // The suggestion is still reported beside the explicit choice, never replacing it.
        Assert.Equal(DnsClientDeviceIconEnum.SingleBoardComputer, created.SuggestedDeviceIconKey);

        // PUT replaces both fields outright.
        HttpResponseMessage replaceResponse = await client.PutAsJsonAsync(
            $"/api/clients/{created.Id}",
            new
            {
                displayName = "Media box",
                isDisabled = false,
                deviceIconKey = "Laptop",
                isFavorite = false,
            }
        );
        Assert.Equal(HttpStatusCode.NoContent, replaceResponse.StatusCode);
        ClientDetail replaced = await client.GetFromJsonAsync<ClientDetail>(
            $"/api/clients/{created.Id}"
        );
        Assert.Equal(DnsClientDeviceIconEnum.Laptop, replaced.DeviceIconKey);
        Assert.False(replaced.IsFavorite);

        // PATCH touches only the fields it names; a null icon clears the explicit choice.
        HttpResponseMessage patchResponse = await client.SendAsync(
            PatchRequest(
                $"/api/clients/{created.Id}",
                new { deviceIconKey = (string)null, isFavorite = true }
            )
        );
        Assert.Equal(HttpStatusCode.NoContent, patchResponse.StatusCode);
        ClientDetail patched = await client.GetFromJsonAsync<ClientDetail>(
            $"/api/clients/{created.Id}"
        );
        Assert.Null(patched.DeviceIconKey);
        Assert.True(patched.IsFavorite);
        Assert.Equal("Media box", patched.DisplayName);
    }

    [Theory]
    [InlineData("Toaster")]
    [InlineData("3")]
    public async Task AnIconOutsideTheCatalog_IsRejectedEverywhere(string deviceIconKey)
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/clients",
            new
            {
                clientIdentifier = $"b8:27:eb:{RandomSuffix()}",
                protocolVersion = "IPv4",
                deviceIconKey,
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, createResponse.StatusCode);

        ClientDetail created = await (
            await client.PostAsJsonAsync(
                "/api/clients",
                new { clientIdentifier = $"b8:27:eb:{RandomSuffix()}", protocolVersion = "IPv4" }
            )
        ).Content.ReadFromJsonAsync<ClientDetail>();

        HttpResponseMessage patchResponse = await client.SendAsync(
            PatchRequest($"/api/clients/{created.Id}", new { deviceIconKey })
        );
        Assert.Equal(HttpStatusCode.BadRequest, patchResponse.StatusCode);

        HttpResponseMessage replaceResponse = await client.PutAsJsonAsync(
            $"/api/clients/{created.Id}",
            new { isDisabled = false, deviceIconKey }
        );
        Assert.Equal(HttpStatusCode.BadRequest, replaceResponse.StatusCode);
    }

    [Fact]
    public async Task TheFavoritesFilter_ListsOnlyStarredClientsWithAMatchingTotal()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        ClientDetail starred = await (
            await client.PostAsJsonAsync(
                "/api/clients",
                new
                {
                    clientIdentifier = $"b8:27:eb:{RandomSuffix()}",
                    protocolVersion = "IPv4",
                    isFavorite = true,
                }
            )
        ).Content.ReadFromJsonAsync<ClientDetail>();
        ClientDetail unstarred = await (
            await client.PostAsJsonAsync(
                "/api/clients",
                new { clientIdentifier = $"b8:27:eb:{RandomSuffix()}", protocolVersion = "IPv4" }
            )
        ).Content.ReadFromJsonAsync<ClientDetail>();

        PagedResult<ClientDetail> favorites = await client.GetFromJsonAsync<
            PagedResult<ClientDetail>
        >("/api/clients?isFavorite=true&skip=0&take=500");
        PagedResult<ClientDetail> others = await client.GetFromJsonAsync<PagedResult<ClientDetail>>(
            "/api/clients?isFavorite=false&skip=0&take=500"
        );

        Assert.All(favorites.Items, item => Assert.True(item.IsFavorite));
        Assert.Contains(favorites.Items, item => item.Id == starred.Id);
        Assert.DoesNotContain(favorites.Items, item => item.Id == unstarred.Id);
        Assert.Contains(others.Items, item => item.Id == unstarred.Id);
        Assert.Equal(favorites.Items.Count, favorites.TotalCount);
    }
}
