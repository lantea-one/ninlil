using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="ClientAddressesController" /> end to end, against a real,
///     in-process host and a real SQLite database (see <see cref="NinlilDnsWebApplicationFactory" />) --
///     adding a further address to an existing known client, listing/single-item reads, a
///     duplicate identifier/protocol pair rejected <c>409</c>, an unknown owning client rejected
///     <c>404</c>, and the "never leave a device with zero addresses" guard on delete.
/// </summary>
[Collection(WebTestCollection.Name)]
public class ClientAddressCrudTests(NinlilDnsWebApplicationFactory factory)
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

    private static async Task<ClientDetail> CreateClientAsync(HttpClient client, string identifier)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/clients",
            new { clientIdentifier = identifier, protocolVersion = "IPv4" }
        );
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ClientDetail>();
    }

    /// <summary>
    ///     This test proves the whole dual-stack merge story this sub-phase exists for: a client
    ///     registered with one IPv4 address can have a second, IPv6 address added directly, and
    ///     both then show up as one device's own address set.
    /// </summary>
    [Fact]
    public async Task AddAddress_ToAnExistingClient_MergesUnderOneDevice()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string ipv4Identifier = $"AA:BB:CC:{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        string ipv6Identifier = $"00:03:00:01:{Guid.NewGuid():N}"[..29];

        ClientDetail created = await CreateClientAsync(client, ipv4Identifier);
        Assert.Single(created.Addresses);

        HttpResponseMessage addResponse = await client.PostAsJsonAsync(
            $"/api/clients/{created.Id}/addresses",
            new { clientIdentifier = ipv6Identifier, protocolVersion = "IPv6" }
        );
        Assert.Equal(HttpStatusCode.Created, addResponse.StatusCode);
        ClientAddressDetail addedAddress =
            await addResponse.Content.ReadFromJsonAsync<ClientAddressDetail>();
        Assert.Equal(created.Id, addedAddress.ClientId);

        HttpResponseMessage getResponse = await client.GetAsync($"/api/clients/{created.Id}");
        getResponse.EnsureSuccessStatusCode();
        ClientDetail fetched = await getResponse.Content.ReadFromJsonAsync<ClientDetail>();

        Assert.Equal(2, fetched.Addresses.Count);
        Assert.Contains(fetched.Addresses, address => address.ClientIdentifier == ipv4Identifier);
        Assert.Contains(fetched.Addresses, address => address.ClientIdentifier == ipv6Identifier);
    }

    [Fact]
    public async Task AddAddress_WithADuplicateIdentifierAndProtocolVersion_ReturnsConflict()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string identifier = $"DD:EE:FF:{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

        ClientDetail firstClient = await CreateClientAsync(client, identifier);
        ClientDetail secondClient = await CreateClientAsync(
            client,
            $"11:22:33:{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}"
        );

        HttpResponseMessage duplicateResponse = await client.PostAsJsonAsync(
            $"/api/clients/{secondClient.Id}/addresses",
            new { clientIdentifier = identifier, protocolVersion = "IPv4" }
        );

        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        Assert.NotEmpty(firstClient.Addresses);
    }

    [Fact]
    public async Task AddAddress_ToANonexistentClient_ReturnsNotFound()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/clients/{Guid.NewGuid()}/addresses",
            new { clientIdentifier = "aa:bb:cc:dd:ee:ff", protocolVersion = "IPv4" }
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAddress_TheDeviceSOwnLastRemainingAddress_ReturnsConflict()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string identifier = $"EE:FF:00:{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

        ClientDetail created = await CreateClientAsync(client, identifier);
        ClientAddressDetail onlyAddress = Assert.Single(created.Addresses);

        HttpResponseMessage deleteResponse = await client.DeleteAsync(
            $"/api/clients/{created.Id}/addresses/{onlyAddress.Id}"
        );

        Assert.Equal(HttpStatusCode.Conflict, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteAddress_WhenAnotherAddressRemains_Succeeds()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string ipv4Identifier = $"FF:00:11:{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        string ipv6Identifier = $"00:03:00:01:{Guid.NewGuid():N}"[..29];

        ClientDetail created = await CreateClientAsync(client, ipv4Identifier);
        HttpResponseMessage addResponse = await client.PostAsJsonAsync(
            $"/api/clients/{created.Id}/addresses",
            new { clientIdentifier = ipv6Identifier, protocolVersion = "IPv6" }
        );
        ClientAddressDetail addedAddress =
            await addResponse.Content.ReadFromJsonAsync<ClientAddressDetail>();

        HttpResponseMessage deleteResponse = await client.DeleteAsync(
            $"/api/clients/{created.Id}/addresses/{addedAddress.Id}"
        );
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        HttpResponseMessage getResponse = await client.GetAsync($"/api/clients/{created.Id}");
        ClientDetail fetched = await getResponse.Content.ReadFromJsonAsync<ClientDetail>();
        Assert.Single(fetched.Addresses);
        Assert.Equal(ipv4Identifier, fetched.Addresses[0].ClientIdentifier);
    }

    [Fact]
    public async Task ListAddresses_ForANonexistentClient_ReturnsNotFound()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync(
            $"/api/clients/{Guid.NewGuid()}/addresses"
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
