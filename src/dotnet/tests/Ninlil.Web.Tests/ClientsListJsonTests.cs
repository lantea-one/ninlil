using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class pins the JSON of <see cref="ClientsController.List" /> over HTTP on the shared
///     in-process host, signed in as the bootstrap administrator. It seeds three clients with
///     fixed ids, creation and sighting times in 2001 and 0, 1 and 3 addresses, then reads the
///     first three clients sorted by creation time, which are these three because other test
///     classes create rows stamped with the real clock. The old times keep the clients offline,
///     out of "new" and behind every real sighting, so tests that count or order clients by
///     presence are not disturbed. The items must equal the text in
///     <see cref="ExpectedItemsJson" />, so how the page loads the addresses cannot change a
///     single field or the order of a client's addresses.
/// </summary>
[Collection(WebTestCollection.Name)]
public class ClientsListJsonTests(NinlilDnsWebApplicationFactory factory)
{
    private static readonly DateTimeOffset Created = new(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly Guid NoAddressesId = new("6f0d7a10-0000-4000-8000-000000000001");
    private static readonly Guid OneAddressId = new("6f0d7a10-0000-4000-8000-000000000002");
    private static readonly Guid ThreeAddressesId = new("6f0d7a10-0000-4000-8000-000000000003");

    /// <summary>
    ///     This constant contains the <c>items</c> array the list must return for the three
    ///     clients seeded by <see cref="SeedAsync" />.
    /// </summary>
    private const string ExpectedItemsJson = """
        [{"id":"6f0d7a10-0000-4000-8000-000000000001","addresses":[],"displayName":"list-json-none","clientGroupId":null,"isDisabled":false,"comments":null,"origin":"Manual","createdAtUtc":"2001-01-01T00:00:00+00:00","updatedAtUtc":null,"pausedUntilUtc":null,"isCurrentlyPaused":false,"lastSeenUtc":null,"isOnline":false,"deviceIconKey":null,"suggestedDeviceIconKey":null,"isFavorite":false,"vendor":null,"isRandomizedMacAddress":false},{"id":"6f0d7a10-0000-4000-8000-000000000002","addresses":[{"id":"6f0d7a11-0000-4000-8000-000000000001","clientId":"6f0d7a10-0000-4000-8000-000000000002","clientIdentifier":"6f:0d:7a:00:00:01","protocolVersion":"IPv4","address":"192.0.2.1","firstSeenUtc":"2001-01-01T00:00:10+00:00","lastSeenUtc":"2001-01-01T00:01:10+00:00","createdAtUtc":"2001-01-01T00:00:10+00:00","macAddress":"6f:0d:7a:00:00:01","vendor":null,"isRandomizedMacAddress":true}],"displayName":"list-json-one","clientGroupId":null,"isDisabled":false,"comments":null,"origin":"Manual","createdAtUtc":"2001-01-01T00:00:01+00:00","updatedAtUtc":null,"pausedUntilUtc":null,"isCurrentlyPaused":false,"lastSeenUtc":"2001-01-01T00:01:10+00:00","isOnline":false,"deviceIconKey":null,"suggestedDeviceIconKey":null,"isFavorite":true,"vendor":null,"isRandomizedMacAddress":true},{"id":"6f0d7a10-0000-4000-8000-000000000003","addresses":[{"id":"6f0d7a11-0000-4000-8000-000000000002","clientId":"6f0d7a10-0000-4000-8000-000000000003","clientIdentifier":"6f:0d:7a:00:00:02","protocolVersion":"IPv4","address":"192.0.2.2","firstSeenUtc":"2001-01-01T00:00:20+00:00","lastSeenUtc":"2001-01-01T00:01:20+00:00","createdAtUtc":"2001-01-01T00:00:20+00:00","macAddress":"6f:0d:7a:00:00:02","vendor":null,"isRandomizedMacAddress":true},{"id":"6f0d7a11-0000-4000-8000-000000000003","clientId":"6f0d7a10-0000-4000-8000-000000000003","clientIdentifier":"6f:0d:7a:00:00:03","protocolVersion":"IPv4","address":"192.0.2.3","firstSeenUtc":"2001-01-01T00:00:30+00:00","lastSeenUtc":"2001-01-01T00:01:30+00:00","createdAtUtc":"2001-01-01T00:00:30+00:00","macAddress":"6f:0d:7a:00:00:03","vendor":null,"isRandomizedMacAddress":true},{"id":"6f0d7a11-0000-4000-8000-000000000004","clientId":"6f0d7a10-0000-4000-8000-000000000003","clientIdentifier":"6f:0d:7a:00:00:04","protocolVersion":"IPv4","address":"192.0.2.4","firstSeenUtc":"2001-01-01T00:00:40+00:00","lastSeenUtc":"2001-01-01T00:01:40+00:00","createdAtUtc":"2001-01-01T00:00:40+00:00","macAddress":"6f:0d:7a:00:00:04","vendor":null,"isRandomizedMacAddress":true}],"displayName":"list-json-three","clientGroupId":null,"isDisabled":false,"comments":"three addresses","origin":"Manual","createdAtUtc":"2001-01-01T00:00:02+00:00","updatedAtUtc":null,"pausedUntilUtc":null,"isCurrentlyPaused":false,"lastSeenUtc":"2001-01-01T00:01:40+00:00","isOnline":false,"deviceIconKey":null,"suggestedDeviceIconKey":null,"isFavorite":false,"vendor":null,"isRandomizedMacAddress":true}]
        """;

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

    private static DnsClientAddressEntity Address(
        string id,
        Guid clientId,
        string identifier,
        string address,
        int seenSeconds
    ) =>
        new()
        {
            Id = new Guid(id),
            CreatedAtUtc = Created.AddSeconds(seenSeconds),
            ClientId = clientId,
            ClientIdentifier = identifier,
            ProtocolVersion = InternetProtocolVersionEnum.IPv4,
            Address = address,
            FirstSeenUtc = Created.AddSeconds(seenSeconds),
            LastSeenUtc = Created.AddSeconds(seenSeconds + 60),
        };

    /// <summary>
    ///     This method saves the three clients and their addresses once; the rows are fixed, so a
    ///     second call finds them and returns.
    /// </summary>
    private async Task SeedAsync()
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        IDnsClientRepository clients =
            scope.ServiceProvider.GetRequiredService<IDnsClientRepository>();
        if (await clients.GetByIdAsync(NoAddressesId) is not null)
            return;

        IDnsClientAddressRepository addresses =
            scope.ServiceProvider.GetRequiredService<IDnsClientAddressRepository>();

        await clients.AddAsync(
            new DnsClientEntity
            {
                Id = NoAddressesId,
                CreatedAtUtc = Created,
                DisplayName = "list-json-none",
            }
        );
        await clients.AddAsync(
            new DnsClientEntity
            {
                Id = OneAddressId,
                CreatedAtUtc = Created.AddSeconds(1),
                DisplayName = "list-json-one",
                IsFavorite = true,
            }
        );
        await clients.AddAsync(
            new DnsClientEntity
            {
                Id = ThreeAddressesId,
                CreatedAtUtc = Created.AddSeconds(2),
                DisplayName = "list-json-three",
                Comments = "three addresses",
            }
        );

        await addresses.AddAsync(
            Address(
                "6f0d7a11-0000-4000-8000-000000000001",
                OneAddressId,
                "6f:0d:7a:00:00:01",
                "192.0.2.1",
                10
            )
        );
        await addresses.AddAsync(
            Address(
                "6f0d7a11-0000-4000-8000-000000000002",
                ThreeAddressesId,
                "6f:0d:7a:00:00:02",
                "192.0.2.2",
                20
            )
        );
        await addresses.AddAsync(
            Address(
                "6f0d7a11-0000-4000-8000-000000000003",
                ThreeAddressesId,
                "6f:0d:7a:00:00:03",
                "192.0.2.3",
                30
            )
        );
        await addresses.AddAsync(
            Address(
                "6f0d7a11-0000-4000-8000-000000000004",
                ThreeAddressesId,
                "6f:0d:7a:00:00:04",
                "192.0.2.4",
                40
            )
        );

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    [Fact]
    public async Task List_ForClientsWithZeroOneAndThreeAddresses_ReturnsTheSameJsonAsBefore()
    {
        await SeedAsync();
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync("/api/clients?sort=Created&take=3");
        response.EnsureSuccessStatusCode();

        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync()
        );
        JsonElement items = document.RootElement.GetProperty("items");

        Assert.Equal(3, items.GetArrayLength());
        Assert.Equal(ExpectedItemsJson, items.GetRawText());
    }
}
