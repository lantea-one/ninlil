using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="BlockedServicesController" /> over HTTP on the shared
///     in-process host, signed in as the bootstrap administrator. It checks that turning a catalog
///     entry on creates an ordinary subscribed block list (visible through
///     <see cref="FilterListsController" />) and turning it off removes it, that both are
///     idempotent, and that a client or client-group target adds an assignment of the shared list
///     for that target only. Each test turns its entries off again in a <c>finally</c> block,
///     since the database is shared with every other test class.
/// </summary>
[Collection(WebTestCollection.Name)]
public class BlockedServicesControllerTests(NinlilDnsWebApplicationFactory factory)
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

    private static async Task<Guid> CreateKnownClientAsync(HttpClient client, string suffix)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/clients",
            new
            {
                clientIdentifier = $"aa:bb:cc:{suffix[..2]}:{suffix[2..4]}:{suffix[4..]}",
                protocolVersion = "IPv4",
            }
        );
        response.EnsureSuccessStatusCode();
        ClientDetail created = await response.Content.ReadFromJsonAsync<ClientDetail>();
        return created.Id;
    }

    private static async Task<Guid> CreateClientGroupAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/client-groups",
            new { name = $"group-{Guid.NewGuid():N}" }
        );
        response.EnsureSuccessStatusCode();
        ClientGroupDetail created = await response.Content.ReadFromJsonAsync<ClientGroupDetail>();
        return created.Id;
    }

    [Fact]
    public async Task List_ReturnsTheRealCatalogWithATikTokEntry()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync("/api/blocked-services");
        response.EnsureSuccessStatusCode();

        IReadOnlyList<BlockedServiceDetail> catalog = await response.Content.ReadFromJsonAsync<
            IReadOnlyList<BlockedServiceDetail>
        >();

        BlockedServiceDetail tiktok = Assert.Single(catalog, entry => entry.Key == "tiktok");
        Assert.Equal("TikTok", tiktok.DisplayName);
        Assert.StartsWith("https://", tiktok.SubscriptionUrl);
    }

    [Fact]
    public async Task EnableThenDisable_CreatesAndRemovesAManagedSubscribedFilterList()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        try
        {
            // Enabling creates a managed, subscribed filter list.
            HttpResponseMessage enableResponse = await client.PostAsync(
                "/api/blocked-services/twitter",
                content: null
            );
            enableResponse.EnsureSuccessStatusCode();
            BlockedServiceDetail enabled =
                await enableResponse.Content.ReadFromJsonAsync<BlockedServiceDetail>();
            Assert.True(enabled.IsEnabled);
            Assert.NotNull(enabled.FilterListId);

            HttpResponseMessage getListResponse = await client.GetAsync(
                $"/api/filter-lists/{enabled.FilterListId}"
            );
            getListResponse.EnsureSuccessStatusCode();
            FilterListDetail managedList =
                await getListResponse.Content.ReadFromJsonAsync<FilterListDetail>();
            Assert.Equal(DnsFilterListSourceEnum.Subscribed, managedList.Source);
            Assert.Equal(DnsFilterListActionEnum.Block, managedList.Action);
            Assert.Equal(enabled.SubscriptionUrl, managedList.SubscriptionUrl);

            // Enabling an enabled entry is idempotent: the same list, not a second one.
            HttpResponseMessage reEnableResponse = await client.PostAsync(
                "/api/blocked-services/twitter",
                content: null
            );
            reEnableResponse.EnsureSuccessStatusCode();
            BlockedServiceDetail reEnabled =
                await reEnableResponse.Content.ReadFromJsonAsync<BlockedServiceDetail>();
            Assert.Equal(enabled.FilterListId, reEnabled.FilterListId);

            // Disabling removes the managed list outright.
            HttpResponseMessage disableResponse = await client.DeleteAsync(
                "/api/blocked-services/twitter"
            );
            Assert.Equal(HttpStatusCode.NoContent, disableResponse.StatusCode);

            HttpResponseMessage getAfterDisableResponse = await client.GetAsync(
                $"/api/filter-lists/{enabled.FilterListId}"
            );
            Assert.Equal(HttpStatusCode.NotFound, getAfterDisableResponse.StatusCode);

            // Disabling an already-disabled entry is a no-op, not an error.
            HttpResponseMessage secondDisableResponse = await client.DeleteAsync(
                "/api/blocked-services/twitter"
            );
            Assert.Equal(HttpStatusCode.NoContent, secondDisableResponse.StatusCode);
        }
        finally
        {
            // Turn the entry off again, since the database is shared with every other test.
            await client.DeleteAsync("/api/blocked-services/twitter");
        }
    }

    [Fact]
    public async Task Enable_WithAnUnknownKey_ReturnsNotFound()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsync(
            "/api/blocked-services/not-a-real-service",
            content: null
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task BlockedServicesEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/blocked-services");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    ///     This test checks per-client targeting. Enabling an entry for one client assigns the
    ///     shared managed list to that client, and the entry reads as enabled for it but not for
    ///     another client. Enabling it for the second client reuses the same list. Disabling it
    ///     for the first client removes only that client's assignment: the list remains and the
    ///     second client still has it.
    /// </summary>
    [Fact]
    public async Task Enable_WithAClientTarget_BlocksOnlyThatClient_AndDisableLeavesTheSharedListInPlace()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        Guid firstClientId = await CreateKnownClientAsync(client, "000001");
        Guid secondClientId = await CreateKnownClientAsync(client, "000002");

        try
        {
            HttpResponseMessage enableResponse = await client.PostAsync(
                $"/api/blocked-services/whatsapp?clientId={firstClientId}",
                content: null
            );
            enableResponse.EnsureSuccessStatusCode();
            BlockedServiceDetail enabledForFirstClient =
                await enableResponse.Content.ReadFromJsonAsync<BlockedServiceDetail>();
            Assert.True(enabledForFirstClient.IsEnabled);
            Assert.NotNull(enabledForFirstClient.FilterListId);

            // For the second client the entry must still read as disabled. The untargeted List
            // reports only whether the shared list exists, so it is not checked here.
            HttpResponseMessage secondClientListResponse = await client.GetAsync(
                $"/api/blocked-services?clientId={secondClientId}"
            );
            IReadOnlyList<BlockedServiceDetail> secondClientCatalog =
                await secondClientListResponse.Content.ReadFromJsonAsync<
                    IReadOnlyList<BlockedServiceDetail>
                >();
            Assert.False(secondClientCatalog.Single(entry => entry.Key == "whatsapp").IsEnabled);

            // The assignment is an ordinary one, visible through ClientListAssignmentsController.
            HttpResponseMessage assignmentsResponse = await client.GetAsync(
                $"/api/clients/{firstClientId}/list-assignments"
            );
            assignmentsResponse.EnsureSuccessStatusCode();
            PagedResult<ClientListAssignmentDetail> assignments =
                await assignmentsResponse.Content.ReadFromJsonAsync<
                    PagedResult<ClientListAssignmentDetail>
                >();
            Assert.Contains(
                assignments.Items,
                assignment => assignment.ListId == enabledForFirstClient.FilterListId
            );

            // Enabling the entry for the second client reuses the same shared list.
            HttpResponseMessage enableForSecondResponse = await client.PostAsync(
                $"/api/blocked-services/whatsapp?clientId={secondClientId}",
                content: null
            );
            enableForSecondResponse.EnsureSuccessStatusCode();
            BlockedServiceDetail enabledForSecondClient =
                await enableForSecondResponse.Content.ReadFromJsonAsync<BlockedServiceDetail>();
            Assert.Equal(enabledForFirstClient.FilterListId, enabledForSecondClient.FilterListId);

            // Disabling for the first client removes only its assignment; the shared list and the
            // second client's assignment stay.
            HttpResponseMessage disableForFirstResponse = await client.DeleteAsync(
                $"/api/blocked-services/whatsapp?clientId={firstClientId}"
            );
            Assert.Equal(HttpStatusCode.NoContent, disableForFirstResponse.StatusCode);

            HttpResponseMessage getListAfterDisableResponse = await client.GetAsync(
                $"/api/filter-lists/{enabledForFirstClient.FilterListId}"
            );
            Assert.Equal(HttpStatusCode.OK, getListAfterDisableResponse.StatusCode);

            HttpResponseMessage firstClientAfterDisableResponse = await client.GetAsync(
                $"/api/blocked-services?clientId={firstClientId}"
            );
            IReadOnlyList<BlockedServiceDetail> firstClientCatalogAfterDisable =
                await firstClientAfterDisableResponse.Content.ReadFromJsonAsync<
                    IReadOnlyList<BlockedServiceDetail>
                >();
            Assert.False(
                firstClientCatalogAfterDisable.Single(entry => entry.Key == "whatsapp").IsEnabled
            );

            HttpResponseMessage secondClientAfterDisableResponse = await client.GetAsync(
                $"/api/blocked-services?clientId={secondClientId}"
            );
            IReadOnlyList<BlockedServiceDetail> secondClientCatalogAfterDisable =
                await secondClientAfterDisableResponse.Content.ReadFromJsonAsync<
                    IReadOnlyList<BlockedServiceDetail>
                >();
            Assert.True(
                secondClientCatalogAfterDisable.Single(entry => entry.Key == "whatsapp").IsEnabled
            );
        }
        finally
        {
            // Remove both assignments, then the shared list with an untargeted disable.
            await client.DeleteAsync($"/api/blocked-services/whatsapp?clientId={firstClientId}");
            await client.DeleteAsync($"/api/blocked-services/whatsapp?clientId={secondClientId}");
            await client.DeleteAsync("/api/blocked-services/whatsapp");
            await client.DeleteAsync($"/api/clients/{firstClientId}");
            await client.DeleteAsync($"/api/clients/{secondClientId}");
        }
    }

    /// <summary>
    ///     This test checks that a client-group target assigns the list to the group, and that
    ///     naming both a client and a group is rejected.
    /// </summary>
    [Fact]
    public async Task Enable_WithAClientGroupTarget_AssignsTheGroup_AndBothTargetsAtOnceIsRejected()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        Guid groupId = await CreateClientGroupAsync(client);
        Guid clientId = await CreateKnownClientAsync(client, "000003");

        try
        {
            HttpResponseMessage bothTargetsResponse = await client.PostAsync(
                $"/api/blocked-services/facebook?clientId={clientId}&clientGroupId={groupId}",
                content: null
            );
            Assert.Equal(HttpStatusCode.BadRequest, bothTargetsResponse.StatusCode);

            HttpResponseMessage enableForGroupResponse = await client.PostAsync(
                $"/api/blocked-services/facebook?clientGroupId={groupId}",
                content: null
            );
            enableForGroupResponse.EnsureSuccessStatusCode();
            BlockedServiceDetail enabledForGroup =
                await enableForGroupResponse.Content.ReadFromJsonAsync<BlockedServiceDetail>();
            Assert.True(enabledForGroup.IsEnabled);

            HttpResponseMessage groupAssignmentsResponse = await client.GetAsync(
                $"/api/client-groups/{groupId}/list-assignments"
            );
            groupAssignmentsResponse.EnsureSuccessStatusCode();
            PagedResult<ClientGroupListAssignmentDetail> groupAssignments =
                await groupAssignmentsResponse.Content.ReadFromJsonAsync<
                    PagedResult<ClientGroupListAssignmentDetail>
                >();
            Assert.Contains(
                groupAssignments.Items,
                assignment => assignment.ListId == enabledForGroup.FilterListId
            );
        }
        finally
        {
            await client.DeleteAsync($"/api/blocked-services/facebook?clientGroupId={groupId}");
            await client.DeleteAsync("/api/blocked-services/facebook");
            await client.DeleteAsync($"/api/clients/{clientId}");
            await client.DeleteAsync($"/api/client-groups/{groupId}");
        }
    }

    [Fact]
    public async Task Enable_WithAnUnknownClientTarget_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsync(
            $"/api/blocked-services/tiktok?clientId={Guid.NewGuid()}",
            content: null
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
