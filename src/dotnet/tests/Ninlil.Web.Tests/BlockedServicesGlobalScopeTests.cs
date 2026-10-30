using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.Filtering;
using Ninlil.Dns.Server.Filtering;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers what a Blocked Services toggle blocks, end to end on the shared
///     in-process host: the REST toggle (<c>/api/blocked-services/{key}</c>, signed in as the
///     bootstrap administrator) and the real <see cref="IDnsFilterEngine" /> that answers each
///     query. A toggle without a client or group blocks the service for every client, including
///     one that no group or client record matches; client and group toggles add to it. The managed
///     list's entries are seeded through the repository, since the host must not fetch the real
///     URL. Each test uses its own service key and source addresses from the documentation ranges
///     (RFC 5737), and turns the service off in a <c>finally</c> block, since the database is
///     shared with every other test class.
/// </summary>
[Collection(WebTestCollection.Name)]
public class BlockedServicesGlobalScopeTests(NinlilDnsWebApplicationFactory factory)
{
    /// <summary>
    ///     A name that is in the seeded entries of the lists these tests create.
    /// </summary>
    private const string BlockedName = "global-scope.example";

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
    ///     This method switches the filter engine on for the running host. The test host does not
    ///     load the runtime settings file, so a PATCH to <c>/api/filtering-settings</c> would not
    ///     reach the live options; an enabled copy in the options cache does.
    ///     <see cref="DisableFiltering" /> removes it, and the monitor builds the configured
    ///     (disabled) options again.
    /// </summary>
    private void EnableFiltering()
    {
        IOptionsMonitorCache<DnsFilterOptions> cache = factory.Services.GetRequiredService<
            IOptionsMonitorCache<DnsFilterOptions>
        >();
        cache.TryRemove(Microsoft.Extensions.Options.Options.DefaultName);
        cache.TryAdd(
            Microsoft.Extensions.Options.Options.DefaultName,
            new DnsFilterOptions { Enable = true }
        );
    }

    /// <summary>
    ///     This method undoes <see cref="EnableFiltering" />.
    /// </summary>
    private void DisableFiltering() =>
        factory
            .Services.GetRequiredService<IOptionsMonitorCache<DnsFilterOptions>>()
            .TryRemove(Microsoft.Extensions.Options.Options.DefaultName);

    /// <summary>
    ///     This method creates a group that matches one source address and returns its id.
    /// </summary>
    private static async Task<Guid> CreateGroupForAddressAsync(HttpClient client, string address)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/client-groups",
            new { name = $"group-{Guid.NewGuid():N}" }
        );
        response.EnsureSuccessStatusCode();
        Guid groupId = (await response.Content.ReadFromJsonAsync<ClientGroupDetail>()).Id;

        HttpResponseMessage networkResponse = await client.PostAsJsonAsync(
            $"/api/client-groups/{groupId}/networks",
            new ClientGroupNetworkWriteRequest($"{address}/32")
        );
        networkResponse.EnsureSuccessStatusCode();

        return groupId;
    }

    /// <summary>
    ///     This method gives a managed list one entry the queries below match.
    /// </summary>
    private async Task SeedEntryAsync(Guid listId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        await scope
            .ServiceProvider.GetRequiredService<IDnsFilterListEntryRepository>()
            .AddAsync(
                new DnsFilterListEntryEntity
                {
                    Id = Guid.NewGuid(),
                    ListId = listId,
                    Value = BlockedName,
                }
            );
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    /// <summary>
    ///     This method asks the engine whether a query for the seeded name from an address is
    ///     blocked.
    /// </summary>
    private async Task<bool> IsBlockedAsync(string address)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        DnsFilterDecision decision = await scope
            .ServiceProvider.GetRequiredService<IDnsFilterEngine>()
            .EvaluateDomainAsync(DnsName.Parse(BlockedName), IPAddress.Parse(address));
        return decision.IsDenied;
    }

    private static async Task<Guid> EnableAsync(HttpClient client, string key, string query = "")
    {
        HttpResponseMessage response = await client.PostAsync(
            $"/api/blocked-services/{key}{query}",
            content: null
        );
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BlockedServiceDetail>())
            .FilterListId
            .Value;
    }

    [Fact]
    public async Task GlobalToggle_BlocksAnUnassignedClient_AndTurningItOffUnblocksIt()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        EnableFiltering();

        try
        {
            Guid listId = await EnableAsync(client, "youtube");
            await SeedEntryAsync(listId);

            Assert.True(await IsBlockedAsync("203.0.113.10"));

            HttpResponseMessage disableResponse = await client.DeleteAsync(
                "/api/blocked-services/youtube"
            );
            Assert.Equal(HttpStatusCode.NoContent, disableResponse.StatusCode);

            Assert.False(await IsBlockedAsync("203.0.113.10"));
        }
        finally
        {
            DisableFiltering();
            await client.DeleteAsync("/api/blocked-services/youtube");
        }
    }

    [Fact]
    public async Task GroupToggle_BlocksOnlyThatGroup_WhileNoGlobalToggleIsOn()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        Guid groupId = await CreateGroupForAddressAsync(client, "198.51.100.20");

        EnableFiltering();

        try
        {
            Guid listId = await EnableAsync(client, "tiktok", $"?clientGroupId={groupId}");
            await SeedEntryAsync(listId);

            Assert.True(await IsBlockedAsync("198.51.100.20"));
            Assert.False(await IsBlockedAsync("203.0.113.20"));

            HttpResponseMessage globalResponse = await client.GetAsync("/api/blocked-services");
            BlockedServiceDetail global = (
                await globalResponse.Content.ReadFromJsonAsync<
                    IReadOnlyList<BlockedServiceDetail>
                >()
            ).Single(entry => entry.Key == "tiktok");
            Assert.False(global.IsEnabled);
        }
        finally
        {
            DisableFiltering();
            await client.DeleteAsync($"/api/blocked-services/tiktok?clientGroupId={groupId}");
            await client.DeleteAsync("/api/blocked-services/tiktok");
            await client.DeleteAsync($"/api/client-groups/{groupId}");
        }
    }

    [Fact]
    public async Task GlobalAndGroupToggles_Compose_ByAddingAndTheGlobalOffKeepsTheGroupsBlock()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        Guid groupId = await CreateGroupForAddressAsync(client, "198.51.100.30");

        EnableFiltering();

        try
        {
            Guid listId = await EnableAsync(client, "facebook", $"?clientGroupId={groupId}");
            await SeedEntryAsync(listId);
            await EnableAsync(client, "facebook");

            // Both on: everyone is blocked, and the group cannot opt out.
            Assert.True(await IsBlockedAsync("198.51.100.30"));
            Assert.True(await IsBlockedAsync("203.0.113.30"));

            // Global off: the group keeps its own block, everyone else is free again.
            Assert.Equal(
                HttpStatusCode.NoContent,
                (await client.DeleteAsync("/api/blocked-services/facebook")).StatusCode
            );
            Assert.True(await IsBlockedAsync("198.51.100.30"));
            Assert.False(await IsBlockedAsync("203.0.113.30"));

            // Group off as well: nothing is blocked and the list is gone.
            await client.DeleteAsync($"/api/blocked-services/facebook?clientGroupId={groupId}");
            Assert.False(await IsBlockedAsync("198.51.100.30"));
        }
        finally
        {
            DisableFiltering();
            await client.DeleteAsync($"/api/blocked-services/facebook?clientGroupId={groupId}");
            await client.DeleteAsync("/api/blocked-services/facebook");
            await client.DeleteAsync($"/api/client-groups/{groupId}");
        }
    }
}
