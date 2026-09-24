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
///     end to end, against a real, in-process host and a real SQLite database. A query log entry
///     is a live artifact the query-answering pipeline itself writes, never an operator-created
///     REST resource, so every entry this test reads back is seeded directly through
///     <see cref="IDnsQueryLogRepository" /> first -- exactly the same "seed the live artifact
///     directly, then only prove the REST surface reads it back correctly" shape
///     <see cref="DhcpLeasesControllerTests" /> already establishes for an identical
///     engine-written-only resource.
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
    ///     This test proves the full nested-resource contract end to end: a known client's own
    ///     activity view returns only the log entries this project's own write-time identity
    ///     resolution attributed to it, correctly paginated, and never leaks an unrelated client's
    ///     own history into it -- even one whose own identifier happens to differ by only the last
    ///     character.
    /// </summary>
    [Fact]
    public async Task List_ReturnsOnlyThisClientsOwnActivity_PaginatedAndUnaffectedByOtherClients()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        string identifierA =
            $"AA:11:22:33:44:{Guid.NewGuid().ToString("N")[..2].ToUpperInvariant()}";
        string identifierB =
            $"AA:11:22:33:55:{Guid.NewGuid().ToString("N")[..2].ToUpperInvariant()}";

        ClientDetail clientA = await CreateClientAsync(client, identifierA);
        ClientDetail clientB = await CreateClientAsync(client, identifierB);

        DateTimeOffset baseTime = DateTimeOffset.UtcNow.AddMinutes(-10);

        // Three entries for client A (to exercise real pagination), one for the unrelated client
        // B, and one with no resolved client identity at all.
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

        // The unfiltered first page (take=2) returns only 2 of client A's own 3 entries.
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

        // Client B's own activity view sees only its own single entry, never client A's or the
        // unattributed one.
        HttpResponseMessage clientBResponse = await client.GetAsync(
            $"/api/clients/{clientB.Id}/activity?take=50"
        );
        clientBResponse.EnsureSuccessStatusCode();
        PagedResult<DnsQueryLogEntryDetail> clientBPage =
            await clientBResponse.Content.ReadFromJsonAsync<PagedResult<DnsQueryLogEntryDetail>>();
        DnsQueryLogEntryDetail onlyEntryForB = Assert.Single(clientBPage.Items);
        Assert.Equal("other-client.example.com", onlyEntryForB.QueriedName);
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
