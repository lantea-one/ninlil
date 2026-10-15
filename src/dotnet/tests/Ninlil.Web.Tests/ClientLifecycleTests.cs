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
///     This class covers what deleting a client or a client group removes, over HTTP on the shared
///     in-process host signed in as the bootstrap administrator:
///     <see cref="ClientsController.Delete" /> and <see cref="ClientGroupsController.Delete" />
///     must leave no schedule, list assignment or scoped Safe Search rewrite of the deleted row,
///     must release the member clients of a group, and must not touch an unrelated client's rows.
///     Leftover rows are looked up through the repositories by id, since the database is shared
///     with other test classes. Each test turns Safe Search off again so no other class sees it.
/// </summary>
[Collection(WebTestCollection.Name)]
public class ClientLifecycleTests(NinlilDnsWebApplicationFactory factory)
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

    private static async Task<Guid> CreateClientAsync(HttpClient client, Guid? groupId = null)
    {
        string identifier = $"aa:12:{TestClientIdentifiers.Tail(4)}";
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/clients",
            new
            {
                clientIdentifier = identifier,
                protocolVersion = "IPv4",
                clientGroupId = groupId,
            }
        );
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ClientDetail>()).Id;
    }

    private static async Task<Guid> CreateGroupAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/client-groups",
            new { name = $"group-{Guid.NewGuid():N}" }
        );
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ClientGroupDetail>()).Id;
    }

    private static async Task<Guid> CreateListAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/filter-lists",
            new
            {
                name = $"lifecycle-{Guid.NewGuid():N}",
                kind = "Domain",
                action = "Block",
                source = "Local",
            }
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<FilterListDetail>()).Id;
    }

    private static async Task AddScheduleAsync(HttpClient client, string ownerRoute)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{ownerRoute}/schedules",
            new
            {
                daysOfWeek = "Monday, Tuesday",
                startTime = "21:00:00",
                endTime = "07:00:00",
                timeZoneId = "Europe/London",
            }
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task<int> CountRewritesAsync(Func<DnsRewriteEntity, bool> match)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        return (
            await scope.ServiceProvider.GetRequiredService<IDnsRewriteRepository>().ListAsync()
        ).Count(match);
    }

    private async Task<DnsClientEntity> GetClientAsync(Guid clientId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        return await scope
            .ServiceProvider.GetRequiredService<IDnsClientRepository>()
            .GetByIdAsync(clientId);
    }

    private async Task<int> CountClientAssignmentsAsync(Guid clientId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        return (
            await scope
                .ServiceProvider.GetRequiredService<IDnsClientListAssignmentRepository>()
                .ListByClientIdAsync(clientId)
        ).Count;
    }

    private async Task<int> CountClientSchedulesAsync(Guid clientId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        return (
            await scope
                .ServiceProvider.GetRequiredService<IDnsClientScheduleRepository>()
                .ListByClientIdAsync(clientId)
        ).Count;
    }

    private async Task<int> CountGroupSchedulesAsync(Guid groupId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        return (
            await scope
                .ServiceProvider.GetRequiredService<IDnsClientGroupScheduleRepository>()
                .ListByClientGroupIdAsync(groupId)
        ).Count;
    }

    [Fact]
    public async Task DeleteGroup_RemovesItsSchedulesAndScopedRewritesAndReleasesMembers()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        Guid groupId = await CreateGroupAsync(client);
        Guid otherGroupId = await CreateGroupAsync(client);
        Guid memberId = await CreateClientAsync(client, groupId);
        Guid unrelatedId = await CreateClientAsync(client, otherGroupId);
        await AddScheduleAsync(client, $"/api/client-groups/{groupId}");
        await AddScheduleAsync(client, $"/api/client-groups/{otherGroupId}");

        try
        {
            Assert.Equal(
                HttpStatusCode.OK,
                (
                    await client.PostAsync($"/api/safe-search/bing?clientGroupId={groupId}", null)
                ).StatusCode
            );
            Assert.Equal(
                HttpStatusCode.OK,
                (
                    await client.PostAsync(
                        $"/api/safe-search/bing?clientGroupId={otherGroupId}",
                        null
                    )
                ).StatusCode
            );
            Assert.NotEqual(0, await CountRewritesAsync(r => r.ClientGroupId == groupId));

            HttpResponseMessage deleteResponse = await client.DeleteAsync(
                $"/api/client-groups/{groupId}"
            );
            Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

            Assert.Equal(0, await CountGroupSchedulesAsync(groupId));
            Assert.Equal(0, await CountRewritesAsync(r => r.ClientGroupId == groupId));
            DnsClientEntity member = await GetClientAsync(memberId);
            Assert.NotNull(member);
            Assert.Null(member.ClientGroupId);

            // The other group, its schedule, its rewrites and its member are untouched.
            Assert.Equal(1, await CountGroupSchedulesAsync(otherGroupId));
            Assert.NotEqual(0, await CountRewritesAsync(r => r.ClientGroupId == otherGroupId));
            Assert.Equal(otherGroupId, (await GetClientAsync(unrelatedId)).ClientGroupId);
        }
        finally
        {
            await client.DeleteAsync($"/api/safe-search/bing?clientGroupId={otherGroupId}");
            await client.DeleteAsync($"/api/client-groups/{otherGroupId}");
        }
    }

    [Fact]
    public async Task DeleteClient_RemovesItsSchedulesAssignmentsAndScopedRewrites()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        Guid clientId = await CreateClientAsync(client);
        Guid unrelatedId = await CreateClientAsync(client);
        Guid listId = await CreateListAsync(client);
        await AddScheduleAsync(client, $"/api/clients/{clientId}");
        await AddScheduleAsync(client, $"/api/clients/{unrelatedId}");

        try
        {
            foreach (Guid id in new[] { clientId, unrelatedId })
            {
                HttpResponseMessage assign = await client.PostAsJsonAsync(
                    $"/api/clients/{id}/list-assignments",
                    new ClientListAssignmentWriteRequest(listId)
                );
                Assert.Equal(HttpStatusCode.Created, assign.StatusCode);
                Assert.Equal(
                    HttpStatusCode.OK,
                    (
                        await client.PostAsync($"/api/safe-search/bing?clientId={id}", null)
                    ).StatusCode
                );
            }

            Assert.Equal(
                HttpStatusCode.NoContent,
                (await client.DeleteAsync($"/api/clients/{clientId}")).StatusCode
            );

            Assert.Equal(0, await CountClientSchedulesAsync(clientId));
            Assert.Equal(0, await CountClientAssignmentsAsync(clientId));
            Assert.Equal(0, await CountRewritesAsync(r => r.ClientId == clientId));

            Assert.Equal(1, await CountClientSchedulesAsync(unrelatedId));
            Assert.Equal(1, await CountClientAssignmentsAsync(unrelatedId));
            Assert.NotEqual(0, await CountRewritesAsync(r => r.ClientId == unrelatedId));
        }
        finally
        {
            await client.DeleteAsync($"/api/safe-search/bing?clientId={unrelatedId}");
            await client.DeleteAsync($"/api/clients/{unrelatedId}");
            await client.DeleteAsync($"/api/filter-lists/{listId}");
        }
    }
}
