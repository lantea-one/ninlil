using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers what deleting a filter list removes, over HTTP on the shared in-process
///     host signed in as the bootstrap administrator: <see cref="FilterListsController.Delete" />
///     and the untargeted <see cref="BlockedServicesController.Disable" /> must leave no entries
///     and no client assignment of the list behind. Entries of a subscribed (Blocked Services)
///     list are seeded through the repository, since the host must not fetch the real URL. Every
///     test finds its own rows by id because the database is shared with other test classes.
/// </summary>
[Collection(WebTestCollection.Name)]
public class FilterListDeleteCascadeTests(NinlilDnsWebApplicationFactory factory)
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

    private static async Task<Guid> CreateClientAsync(HttpClient client, string suffix)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/clients",
            new
            {
                clientIdentifier = $"aa:bb:cd:{suffix[..2]}:{suffix[2..4]}:{suffix[4..]}",
                protocolVersion = "IPv4",
            }
        );
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ClientDetail>()).Id;
    }

    private async Task SeedEntriesAsync(Guid listId, int count)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        await scope
            .ServiceProvider.GetRequiredService<IDnsFilterListEntryRepository>()
            .AddRangeAsync([
                .. Enumerable
                    .Range(0, count)
                    .Select(index => new DnsFilterListEntryEntity
                    {
                        Id = Guid.NewGuid(),
                        ListId = listId,
                        Value = $"seeded{index}.example",
                    }),
            ]);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    private async Task<int> CountEntriesAsync(Guid listId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        return await scope
            .ServiceProvider.GetRequiredService<IDnsFilterListEntryRepository>()
            .CountByListIdAsync(listId);
    }

    private async Task<int> CountAssignmentsAsync(Guid listId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        IServiceProvider services = scope.ServiceProvider;
        return (
                await services
                    .GetRequiredService<IDnsClientListAssignmentRepository>()
                    .ListAsync(assignment => assignment.ListId == listId)
            ).Count
            + (
                await services
                    .GetRequiredService<IDnsClientGroupListAssignmentRepository>()
                    .ListAsync(assignment => assignment.ListId == listId)
            ).Count;
    }

    [Fact]
    public async Task Delete_OfAListWithEntriesAndAClientAssignment_RemovesBoth()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        Guid clientId = await CreateClientAsync(client, "100001");

        try
        {
            HttpResponseMessage createResponse = await client.PostAsJsonAsync(
                "/api/filter-lists",
                new
                {
                    name = $"cascade-{Guid.NewGuid():N}",
                    kind = "Domain",
                    action = "Block",
                    source = "Local",
                }
            );
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
            Guid listId = (await createResponse.Content.ReadFromJsonAsync<FilterListDetail>()).Id;

            HttpResponseMessage entryResponse = await client.PostAsJsonAsync(
                $"/api/filter-lists/{listId}/entries",
                new { value = "cascade.example.com" }
            );
            Assert.Equal(HttpStatusCode.Created, entryResponse.StatusCode);
            HttpResponseMessage assignResponse = await client.PostAsJsonAsync(
                $"/api/clients/{clientId}/list-assignments",
                new ClientListAssignmentWriteRequest(listId)
            );
            Assert.Equal(HttpStatusCode.Created, assignResponse.StatusCode);
            Assert.Equal(1, await CountEntriesAsync(listId));
            Assert.Equal(1, await CountAssignmentsAsync(listId));

            HttpResponseMessage deleteResponse = await client.DeleteAsync(
                $"/api/filter-lists/{listId}"
            );
            Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

            Assert.Equal(
                HttpStatusCode.NotFound,
                (await client.GetAsync($"/api/filter-lists/{listId}/entries")).StatusCode
            );
            Assert.Equal(0, await CountEntriesAsync(listId));
            Assert.Equal(0, await CountAssignmentsAsync(listId));

            HttpResponseMessage assignmentsResponse = await client.GetAsync(
                $"/api/clients/{clientId}/list-assignments"
            );
            assignmentsResponse.EnsureSuccessStatusCode();
            PagedResult<ClientListAssignmentDetail> assignments =
                await assignmentsResponse.Content.ReadFromJsonAsync<
                    PagedResult<ClientListAssignmentDetail>
                >();
            Assert.DoesNotContain(assignments.Items, assignment => assignment.ListId == listId);
        }
        finally
        {
            await client.DeleteAsync($"/api/clients/{clientId}");
        }
    }

    [Fact]
    public async Task Disable_WithoutATarget_RemovesTheManagedListsEntriesAndAssignments()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        Guid clientId = await CreateClientAsync(client, "100002");

        try
        {
            HttpResponseMessage enableResponse = await client.PostAsync(
                $"/api/blocked-services/twitter?clientId={clientId}",
                content: null
            );
            enableResponse.EnsureSuccessStatusCode();
            Guid listId = (await enableResponse.Content.ReadFromJsonAsync<BlockedServiceDetail>())
                .FilterListId
                .Value;
            await SeedEntriesAsync(listId, 25);
            Assert.Equal(25, await CountEntriesAsync(listId));
            Assert.Equal(1, await CountAssignmentsAsync(listId));

            HttpResponseMessage disableResponse = await client.DeleteAsync(
                "/api/blocked-services/twitter"
            );
            Assert.Equal(HttpStatusCode.NoContent, disableResponse.StatusCode);

            Assert.Equal(
                HttpStatusCode.NotFound,
                (await client.GetAsync($"/api/filter-lists/{listId}")).StatusCode
            );
            Assert.Equal(0, await CountEntriesAsync(listId));
            Assert.Equal(0, await CountAssignmentsAsync(listId));
        }
        finally
        {
            await client.DeleteAsync("/api/blocked-services/twitter");
            await client.DeleteAsync($"/api/clients/{clientId}");
        }
    }
}
