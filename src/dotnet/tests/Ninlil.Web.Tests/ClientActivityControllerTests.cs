using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="ClientActivityController" /> (<c>/api/clients/{clientId}/activity</c>)
///     over HTTP on the shared in-process host, signed in as the bootstrap administrator. Query
///     log entries are written by the DNS pipeline, not through the API, so the tests save them
///     directly through <see cref="IDnsQueryLogRepository" /> and then check what the endpoint
///     returns, as <see cref="DhcpLeasesControllerTests" /> does for leases.
/// </summary>
[Collection(WebTestCollection.Name)]
public class ClientActivityControllerTests(NinlilDnsWebApplicationFactory factory)
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

    private async Task<ClientDetail> CreateClientAsync(HttpClient client, string identifier)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/clients",
            new { clientIdentifier = identifier, protocolVersion = "IPv4" }
        );
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ClientDetail>();
    }

    private async Task SeedQueryLogEntryAsync(
        string clientIdentifier,
        InternetProtocolVersionEnum? clientProtocolVersion,
        string queriedName,
        DateTimeOffset occurredAtUtc
    )
    {
        using IServiceScope scope = factory.Services.CreateScope();
        IDnsQueryLogRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsQueryLogRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await repository.AddAsync(
            new DnsQueryLogEntryEntity
            {
                Id = Guid.NewGuid(),
                CreatedAtUtc = occurredAtUtc,
                OccurredAtUtc = occurredAtUtc,
                ClientAddress = "203.0.113.55",
                Protocol = DnsTransportProtocolEnum.Udp,
                QueriedName = queriedName,
                QueriedType = DnsResourceRecordTypeEnum.A,
                ResponseCode = DnsResponseCodeEnum.NoError,
                AnswerCount = 1,
                ClientIdentifier = clientIdentifier,
                ClientProtocolVersion = clientProtocolVersion,
            }
        );

        await unitOfWork.SaveChangesAsync();
    }

    /// <summary>
    ///     This test checks that a client's activity holds only the log entries attributed to its
    ///     identifier, paginated, and nothing from another client with a similar identifier or
    ///     from an entry with no client identity.
    /// </summary>
    [Fact]
    public async Task List_ReturnsOnlyThisClientsOwnActivity_PaginatedAndUnaffectedByOtherClients()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        string identifierA = $"aa:11:22:33:44:{TestClientIdentifiers.Tail(1)}";
        string identifierB = $"aa:11:22:33:55:{TestClientIdentifiers.Tail(1)}";

        ClientDetail clientA = await CreateClientAsync(client, identifierA);
        ClientDetail clientB = await CreateClientAsync(client, identifierB);

        DateTimeOffset baseTime = DateTimeOffset.UtcNow.AddMinutes(-10);

        // Three entries for client A (enough to paginate), one for client B, and one with no
        // client identity.
        await SeedQueryLogEntryAsync(
            identifierA,
            InternetProtocolVersionEnum.IPv4,
            "one.example.com",
            baseTime
        );
        await SeedQueryLogEntryAsync(
            identifierA,
            InternetProtocolVersionEnum.IPv4,
            "two.example.com",
            baseTime.AddSeconds(1)
        );
        await SeedQueryLogEntryAsync(
            identifierA,
            InternetProtocolVersionEnum.IPv4,
            "three.example.com",
            baseTime.AddSeconds(2)
        );
        await SeedQueryLogEntryAsync(
            identifierB,
            InternetProtocolVersionEnum.IPv4,
            "other-client.example.com",
            baseTime.AddSeconds(3)
        );
        await SeedQueryLogEntryAsync(
            null,
            null,
            "unattributed.example.com",
            baseTime.AddSeconds(4)
        );

        // The first page (take=2) returns 2 of client A's 3 entries.
        HttpResponseMessage firstPageResponse = await client.GetAsync(
            $"/api/clients/{clientA.Id}/activity?skip=0&take=2"
        );
        firstPageResponse.EnsureSuccessStatusCode();
        PagedResult<DnsQueryLogEntryDetail> firstPage =
            await firstPageResponse.Content.ReadFromJsonAsync<
                PagedResult<DnsQueryLogEntryDetail>
            >();
        Assert.Equal(3, firstPage.TotalCount);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.All(firstPage.Items, entry => Assert.Equal(identifierA, entry.ClientIdentifier));

        // The second page returns the remaining entry.
        HttpResponseMessage secondPageResponse = await client.GetAsync(
            $"/api/clients/{clientA.Id}/activity?skip=2&take=2"
        );
        secondPageResponse.EnsureSuccessStatusCode();
        PagedResult<DnsQueryLogEntryDetail> secondPage =
            await secondPageResponse.Content.ReadFromJsonAsync<
                PagedResult<DnsQueryLogEntryDetail>
            >();
        Assert.Single(secondPage.Items);
        Assert.Equal(identifierA, secondPage.Items[0].ClientIdentifier);

        // Client B sees only its own entry, not client A's or the unattributed one.
        HttpResponseMessage clientBResponse = await client.GetAsync(
            $"/api/clients/{clientB.Id}/activity?take=50"
        );
        clientBResponse.EnsureSuccessStatusCode();
        PagedResult<DnsQueryLogEntryDetail> clientBPage =
            await clientBResponse.Content.ReadFromJsonAsync<PagedResult<DnsQueryLogEntryDetail>>();
        DnsQueryLogEntryDetail onlyEntryForB = Assert.Single(clientBPage.Items);
        Assert.Equal("other-client.example.com", onlyEntryForB.QueriedName);
    }

    /// <summary>
    ///     This test checks that a client saved with no address gets an empty page (200, total 0)
    ///     and not the whole query log, even when other entries exist.
    /// </summary>
    [Fact]
    public async Task List_ForAClientWithNoAddresses_ReturnsAnEmptyPage()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        Guid clientId = Guid.NewGuid();
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IDnsClientRepository repository =
                scope.ServiceProvider.GetRequiredService<IDnsClientRepository>();
            await repository.AddAsync(
                new DnsClientEntity
                {
                    Id = clientId,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    DisplayName = "no-address-client",
                }
            );
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await SeedQueryLogEntryAsync(
            "AA:99:99:99:99:99",
            InternetProtocolVersionEnum.IPv4,
            "someone-else.example.com",
            DateTimeOffset.UtcNow.AddMinutes(-1)
        );

        HttpResponseMessage response = await client.GetAsync($"/api/clients/{clientId}/activity");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        PagedResult<DnsQueryLogEntryDetail> page = await response.Content.ReadFromJsonAsync<
            PagedResult<DnsQueryLogEntryDetail>
        >();
        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task List_ForANonexistentClient_ReturnsNotFound()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync(
            $"/api/clients/{Guid.NewGuid()}/activity"
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ClientActivityEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(
            $"/api/clients/{Guid.NewGuid()}/activity"
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
