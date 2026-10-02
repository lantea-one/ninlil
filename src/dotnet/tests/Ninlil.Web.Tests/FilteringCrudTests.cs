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
///     This class covers the filtering endpoints over HTTP on the shared in-process host, signed
///     in as the bootstrap administrator: the lifecycles of <see cref="FilterListsController" />,
///     <see cref="FilterListEntriesController" />, <see cref="ClientGroupsController" />,
///     <see cref="ClientGroupNetworksController" /> and <see cref="ClientGroupListAssignmentsController" />,
///     entry kinds (domain, wildcard, regex), a subscribed list rejecting manual entries with
///     <c>409 Conflict</c>, a single default client group, and deleting a filter list or client
///     group removing its assignments and networks. Whether a deleted list's entries are removed
///     is not checked.
/// </summary>
[Collection(WebTestCollection.Name)]
public class FilteringCrudTests(NinlilDnsWebApplicationFactory factory)
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

    private static HttpRequestMessage PatchRequest(string url, object body) =>
        new(HttpMethod.Patch, url)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json"
            ),
        };

    [Fact]
    public async Task FilterListLifecycle_CreateEditEntriesAndDelete_WorksEndToEnd()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string name = $"filterlist-{Guid.NewGuid():N}";

        // Creating a local, domain-kind block list.
        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/filter-lists",
            new
            {
                name,
                kind = "Domain",
                action = "Block",
                source = "Local",
                comments = "created by test",
            }
        );
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        FilterListDetail created =
            await createResponse.Content.ReadFromJsonAsync<FilterListDetail>();
        Assert.Equal(name, created.Name);
        Assert.Equal(DnsFilterListSourceEnum.Local, created.Source);
        Assert.NotNull(created.OriginatingNodeId);

        // Duplicate name must be rejected.
        HttpResponseMessage duplicateResponse = await client.PostAsJsonAsync(
            "/api/filter-lists",
            new
            {
                name,
                kind = "Domain",
                action = "Block",
                source = "Local",
            }
        );
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);

        // A subscribed list without a subscriptionUrl must be rejected.
        HttpResponseMessage missingUrlResponse = await client.PostAsJsonAsync(
            "/api/filter-lists",
            new
            {
                name = $"subscribed-{Guid.NewGuid():N}",
                kind = "Domain",
                action = "Block",
                source = "Subscribed",
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, missingUrlResponse.StatusCode);

        // Add an entry to the local list.
        HttpResponseMessage entryCreateResponse = await client.PostAsJsonAsync(
            $"/api/filter-lists/{created.Id}/entries",
            new { value = "ads.example.com", comment = "test entry" }
        );
        Assert.Equal(HttpStatusCode.Created, entryCreateResponse.StatusCode);
        FilterListEntryDetail entry =
            await entryCreateResponse.Content.ReadFromJsonAsync<FilterListEntryDetail>();
        Assert.Equal("ads.example.com", entry.Value);
        Assert.NotNull(entry.OriginatingNodeId);

        // Only a subscribed list's refresh ever produces an exception entry.
        Assert.False(entry.IsException);

        // Listing entries reflects it, with pagination fields present.
        HttpResponseMessage entryListResponse = await client.GetAsync(
            $"/api/filter-lists/{created.Id}/entries"
        );
        entryListResponse.EnsureSuccessStatusCode();
        PagedResult<FilterListEntryDetail> entryPage =
            await entryListResponse.Content.ReadFromJsonAsync<PagedResult<FilterListEntryDetail>>();
        Assert.Contains(entryPage.Items, item => item.Id == entry.Id);

        // PUT replaces the entry's value and keeps its identifier.
        HttpResponseMessage entryReplaceResponse = await client.PutAsJsonAsync(
            $"/api/filter-lists/{created.Id}/entries/{entry.Id}",
            new { value = "tracking.example.com", comment = "replaced" }
        );
        Assert.Equal(HttpStatusCode.NoContent, entryReplaceResponse.StatusCode);

        HttpResponseMessage entryGetResponse = await client.GetAsync(
            $"/api/filter-lists/{created.Id}/entries/{entry.Id}"
        );
        FilterListEntryDetail replacedEntry =
            await entryGetResponse.Content.ReadFromJsonAsync<FilterListEntryDetail>();
        Assert.Equal("tracking.example.com", replacedEntry.Value);
        Assert.Equal(entry.Id, replacedEntry.Id);

        // PATCH the list's metadata, not its entries.
        HttpResponseMessage listPatchResponse = await client.SendAsync(
            PatchRequest($"/api/filter-lists/{created.Id}", new { isDisabled = true })
        );
        Assert.Equal(HttpStatusCode.NoContent, listPatchResponse.StatusCode);

        HttpResponseMessage afterPatchResponse = await client.GetAsync(
            $"/api/filter-lists/{created.Id}"
        );
        FilterListDetail patchedList =
            await afterPatchResponse.Content.ReadFromJsonAsync<FilterListDetail>();
        Assert.True(patchedList.IsDisabled);
        Assert.Equal(name, patchedList.Name);

        // Delete the entry.
        HttpResponseMessage entryDeleteResponse = await client.DeleteAsync(
            $"/api/filter-lists/{created.Id}/entries/{entry.Id}"
        );
        Assert.Equal(HttpStatusCode.NoContent, entryDeleteResponse.StatusCode);

        HttpResponseMessage entryAfterDeleteResponse = await client.GetAsync(
            $"/api/filter-lists/{created.Id}/entries/{entry.Id}"
        );
        Assert.Equal(HttpStatusCode.NotFound, entryAfterDeleteResponse.StatusCode);

        // Delete the list itself.
        HttpResponseMessage deleteResponse = await client.DeleteAsync(
            $"/api/filter-lists/{created.Id}"
        );
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        HttpResponseMessage afterDeleteResponse = await client.GetAsync(
            $"/api/filter-lists/{created.Id}"
        );
        Assert.Equal(HttpStatusCode.NotFound, afterDeleteResponse.StatusCode);
    }

    [Fact]
    public async Task SubscribedFilterList_RejectsManualEntryWrites_WithConflict()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/filter-lists",
            new
            {
                name = $"subscribed-{Guid.NewGuid():N}",
                kind = "Domain",
                action = "Block",
                source = "Subscribed",
                subscriptionUrl = "https://example.invalid/hosts.txt",
            }
        );
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        FilterListDetail list = await createResponse.Content.ReadFromJsonAsync<FilterListDetail>();

        // A manual entry in a subscribed list must be rejected, because the next refresh would
        // silently discard it (see FilterListEntriesController).
        HttpResponseMessage createEntryResponse = await client.PostAsJsonAsync(
            $"/api/filter-lists/{list.Id}/entries",
            new { value = "manual.example.com" }
        );
        Assert.Equal(HttpStatusCode.Conflict, createEntryResponse.StatusCode);
    }

    [Fact]
    public async Task FilterListEntry_WildcardAndRegexKinds_CreateAndRoundTripCorrectly()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage listResponse = await client.PostAsJsonAsync(
            "/api/filter-lists",
            new
            {
                name = $"kinds-{Guid.NewGuid():N}",
                kind = "Domain",
                action = "Block",
                source = "Local",
            }
        );
        FilterListDetail list = await listResponse.Content.ReadFromJsonAsync<FilterListDetail>();

        HttpResponseMessage wildcardCreateResponse = await client.PostAsJsonAsync(
            $"/api/filter-lists/{list.Id}/entries",
            new { value = "ads.*.example.com", kind = "Wildcard" }
        );
        Assert.Equal(HttpStatusCode.Created, wildcardCreateResponse.StatusCode);
        FilterListEntryDetail wildcardEntry =
            await wildcardCreateResponse.Content.ReadFromJsonAsync<FilterListEntryDetail>();
        Assert.Equal(DnsFilterEntryKindEnum.Wildcard, wildcardEntry.Kind);

        HttpResponseMessage regexCreateResponse = await client.PostAsJsonAsync(
            $"/api/filter-lists/{list.Id}/entries",
            new { value = @"^ads?\.example\.com$", kind = "Regex" }
        );
        Assert.Equal(HttpStatusCode.Created, regexCreateResponse.StatusCode);
        FilterListEntryDetail regexEntry =
            await regexCreateResponse.Content.ReadFromJsonAsync<FilterListEntryDetail>();
        Assert.Equal(DnsFilterEntryKindEnum.Regex, regexEntry.Kind);

        // An entry without "kind" defaults to Domain.
        HttpResponseMessage plainCreateResponse = await client.PostAsJsonAsync(
            $"/api/filter-lists/{list.Id}/entries",
            new { value = "plain.example.com" }
        );
        Assert.Equal(HttpStatusCode.Created, plainCreateResponse.StatusCode);
        FilterListEntryDetail plainEntry =
            await plainCreateResponse.Content.ReadFromJsonAsync<FilterListEntryDetail>();
        Assert.Equal(DnsFilterEntryKindEnum.Domain, plainEntry.Kind);

        // GET reflects the persisted kind, not just the creation response.
        HttpResponseMessage wildcardGetResponse = await client.GetAsync(
            $"/api/filter-lists/{list.Id}/entries/{wildcardEntry.Id}"
        );
        FilterListEntryDetail wildcardFromGet =
            await wildcardGetResponse.Content.ReadFromJsonAsync<FilterListEntryDetail>();
        Assert.Equal(DnsFilterEntryKindEnum.Wildcard, wildcardFromGet.Kind);

        // PUT can change an entry's kind and keeps its identifier.
        HttpResponseMessage replaceResponse = await client.PutAsJsonAsync(
            $"/api/filter-lists/{list.Id}/entries/{wildcardEntry.Id}",
            new { value = @"tracker\.example\.com", kind = "Regex" }
        );
        Assert.Equal(HttpStatusCode.NoContent, replaceResponse.StatusCode);

        HttpResponseMessage afterReplaceResponse = await client.GetAsync(
            $"/api/filter-lists/{list.Id}/entries/{wildcardEntry.Id}"
        );
        FilterListEntryDetail afterReplace =
            await afterReplaceResponse.Content.ReadFromJsonAsync<FilterListEntryDetail>();
        Assert.Equal(DnsFilterEntryKindEnum.Regex, afterReplace.Kind);
        Assert.Equal(wildcardEntry.Id, afterReplace.Id);
    }

    [Fact]
    public async Task FilterListEntry_MalformedRegexOrWildcard_IsRejectedWithBadRequestAndNeverPersisted()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage listResponse = await client.PostAsJsonAsync(
            "/api/filter-lists",
            new
            {
                name = $"malformed-{Guid.NewGuid():N}",
                kind = "Domain",
                action = "Block",
                source = "Local",
            }
        );
        FilterListDetail list = await listResponse.Content.ReadFromJsonAsync<FilterListDetail>();

        // "(" is not a valid .NET regular expression. Despite the test name, no malformed
        // wildcard is tried.
        HttpResponseMessage malformedRegexResponse = await client.PostAsJsonAsync(
            $"/api/filter-lists/{list.Id}/entries",
            new { value = "(", kind = "Regex" }
        );
        Assert.Equal(HttpStatusCode.BadRequest, malformedRegexResponse.StatusCode);

        HttpResponseMessage entriesAfterMalformedResponse = await client.GetAsync(
            $"/api/filter-lists/{list.Id}/entries"
        );
        PagedResult<FilterListEntryDetail> entriesAfterMalformed =
            await entriesAfterMalformedResponse.Content.ReadFromJsonAsync<
                PagedResult<FilterListEntryDetail>
            >();
        Assert.Empty(entriesAfterMalformed.Items);
    }

    [Fact]
    public async Task FilterListEntry_WildcardOrRegexKind_IsRejectedAgainstANonDomainList()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage listResponse = await client.PostAsJsonAsync(
            "/api/filter-lists",
            new
            {
                name = $"client-source-{Guid.NewGuid():N}",
                kind = "ClientSource",
                action = "Block",
                source = "Local",
            }
        );
        FilterListDetail list = await listResponse.Content.ReadFromJsonAsync<FilterListDetail>();

        HttpResponseMessage wildcardOnNetworkListResponse = await client.PostAsJsonAsync(
            $"/api/filter-lists/{list.Id}/entries",
            new { value = "10.0.0.*", kind = "Wildcard" }
        );
        Assert.Equal(HttpStatusCode.BadRequest, wildcardOnNetworkListResponse.StatusCode);

        // The default Domain kind is accepted for a network entry.
        HttpResponseMessage domainKindOnNetworkListResponse = await client.PostAsJsonAsync(
            $"/api/filter-lists/{list.Id}/entries",
            new { value = "10.0.0.5" }
        );
        Assert.Equal(HttpStatusCode.Created, domainKindOnNetworkListResponse.StatusCode);
    }

    [Fact]
    public async Task DeletingFilterList_RemovesItsOwnClientGroupAssignments()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage listResponse = await client.PostAsJsonAsync(
            "/api/filter-lists",
            new
            {
                name = $"cascade-list-{Guid.NewGuid():N}",
                kind = "Domain",
                action = "Block",
                source = "Local",
            }
        );
        FilterListDetail list = await listResponse.Content.ReadFromJsonAsync<FilterListDetail>();

        HttpResponseMessage groupResponse = await client.PostAsJsonAsync(
            "/api/client-groups",
            new { name = $"cascade-group-{Guid.NewGuid():N}" }
        );
        ClientGroupDetail group =
            await groupResponse.Content.ReadFromJsonAsync<ClientGroupDetail>();

        HttpResponseMessage assignmentResponse = await client.PostAsJsonAsync(
            $"/api/client-groups/{group.Id}/list-assignments",
            new { listId = list.Id }
        );
        Assert.Equal(HttpStatusCode.Created, assignmentResponse.StatusCode);
        ClientGroupListAssignmentDetail assignment =
            await assignmentResponse.Content.ReadFromJsonAsync<ClientGroupListAssignmentDetail>();

        // Deleting the list must remove the dangling assignment too.
        HttpResponseMessage deleteListResponse = await client.DeleteAsync(
            $"/api/filter-lists/{list.Id}"
        );
        Assert.Equal(HttpStatusCode.NoContent, deleteListResponse.StatusCode);

        HttpResponseMessage assignmentAfterDeleteResponse = await client.GetAsync(
            $"/api/client-groups/{group.Id}/list-assignments/{assignment.Id}"
        );
        Assert.Equal(HttpStatusCode.NotFound, assignmentAfterDeleteResponse.StatusCode);
    }

    [Fact]
    public async Task ClientGroupLifecycle_CreateNetworksAssignmentsAndDelete_WorksEndToEnd()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string name = $"group-{Guid.NewGuid():N}";

        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/client-groups",
            new { name }
        );
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        ClientGroupDetail created =
            await createResponse.Content.ReadFromJsonAsync<ClientGroupDetail>();
        Assert.False(created.IsDefault);

        // Duplicate name must be rejected.
        HttpResponseMessage duplicateResponse = await client.PostAsJsonAsync(
            "/api/client-groups",
            new { name }
        );
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);

        // Add a network.
        HttpResponseMessage networkCreateResponse = await client.PostAsJsonAsync(
            $"/api/client-groups/{created.Id}/networks",
            new { cidr = "192.0.2.0/24" }
        );
        Assert.Equal(HttpStatusCode.Created, networkCreateResponse.StatusCode);
        ClientGroupNetworkDetail network =
            await networkCreateResponse.Content.ReadFromJsonAsync<ClientGroupNetworkDetail>();
        Assert.Equal("192.0.2.0/24", network.Cidr);

        // An invalid CIDR must be rejected.
        HttpResponseMessage invalidCidrResponse = await client.PostAsJsonAsync(
            $"/api/client-groups/{created.Id}/networks",
            new { cidr = "not-a-network" }
        );
        Assert.Equal(HttpStatusCode.BadRequest, invalidCidrResponse.StatusCode);

        // PUT replaces the network's own CIDR, keeping its identifier.
        HttpResponseMessage networkReplaceResponse = await client.PutAsJsonAsync(
            $"/api/client-groups/{created.Id}/networks/{network.Id}",
            new { cidr = "198.51.100.0/24" }
        );
        Assert.Equal(HttpStatusCode.NoContent, networkReplaceResponse.StatusCode);

        HttpResponseMessage networkGetResponse = await client.GetAsync(
            $"/api/client-groups/{created.Id}/networks/{network.Id}"
        );
        ClientGroupNetworkDetail replacedNetwork =
            await networkGetResponse.Content.ReadFromJsonAsync<ClientGroupNetworkDetail>();
        Assert.Equal("198.51.100.0/24", replacedNetwork.Cidr);
        Assert.Equal(network.Id, replacedNetwork.Id);

        // Create a filter list to assign.
        HttpResponseMessage listResponse = await client.PostAsJsonAsync(
            "/api/filter-lists",
            new
            {
                name = $"assign-list-{Guid.NewGuid():N}",
                kind = "Domain",
                action = "Block",
                source = "Local",
            }
        );
        FilterListDetail list = await listResponse.Content.ReadFromJsonAsync<FilterListDetail>();

        HttpResponseMessage assignmentResponse = await client.PostAsJsonAsync(
            $"/api/client-groups/{created.Id}/list-assignments",
            new { listId = list.Id }
        );
        Assert.Equal(HttpStatusCode.Created, assignmentResponse.StatusCode);
        ClientGroupListAssignmentDetail assignment =
            await assignmentResponse.Content.ReadFromJsonAsync<ClientGroupListAssignmentDetail>();
        Assert.Equal(list.Id, assignment.ListId);

        // Assigning the same list twice must be rejected.
        HttpResponseMessage duplicateAssignmentResponse = await client.PostAsJsonAsync(
            $"/api/client-groups/{created.Id}/list-assignments",
            new { listId = list.Id }
        );
        Assert.Equal(HttpStatusCode.Conflict, duplicateAssignmentResponse.StatusCode);

        // Assigning a non-existent list must be rejected.
        HttpResponseMessage missingListAssignmentResponse = await client.PostAsJsonAsync(
            $"/api/client-groups/{created.Id}/list-assignments",
            new { listId = Guid.NewGuid() }
        );
        Assert.Equal(HttpStatusCode.BadRequest, missingListAssignmentResponse.StatusCode);

        // PATCH makes this group the default; at most one group may be the default (see
        // ClientGroupsController.DemoteExistingDefaultAsync).
        HttpResponseMessage patchResponse = await client.SendAsync(
            PatchRequest($"/api/client-groups/{created.Id}", new { isDefault = true })
        );
        Assert.Equal(HttpStatusCode.NoContent, patchResponse.StatusCode);

        HttpResponseMessage afterPatchResponse = await client.GetAsync(
            $"/api/client-groups/{created.Id}"
        );
        ClientGroupDetail patchedGroup =
            await afterPatchResponse.Content.ReadFromJsonAsync<ClientGroupDetail>();
        Assert.True(patchedGroup.IsDefault);

        // Delete the network and the assignment explicitly, then the group itself.
        HttpResponseMessage networkDeleteResponse = await client.DeleteAsync(
            $"/api/client-groups/{created.Id}/networks/{network.Id}"
        );
        Assert.Equal(HttpStatusCode.NoContent, networkDeleteResponse.StatusCode);

        HttpResponseMessage assignmentDeleteResponse = await client.DeleteAsync(
            $"/api/client-groups/{created.Id}/list-assignments/{assignment.Id}"
        );
        Assert.Equal(HttpStatusCode.NoContent, assignmentDeleteResponse.StatusCode);

        HttpResponseMessage deleteResponse = await client.DeleteAsync(
            $"/api/client-groups/{created.Id}"
        );
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        HttpResponseMessage afterDeleteResponse = await client.GetAsync(
            $"/api/client-groups/{created.Id}"
        );
        Assert.Equal(HttpStatusCode.NotFound, afterDeleteResponse.StatusCode);
    }

    [Fact]
    public async Task PromotingAClientGroupToDefault_DemotesTheExistingDefault()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage firstResponse = await client.PostAsJsonAsync(
            "/api/client-groups",
            new { name = $"default-a-{Guid.NewGuid():N}", isDefault = true }
        );
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        ClientGroupDetail first =
            await firstResponse.Content.ReadFromJsonAsync<ClientGroupDetail>();
        Assert.True(first.IsDefault);

        HttpResponseMessage secondResponse = await client.PostAsJsonAsync(
            "/api/client-groups",
            new { name = $"default-b-{Guid.NewGuid():N}", isDefault = true }
        );
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        ClientGroupDetail second =
            await secondResponse.Content.ReadFromJsonAsync<ClientGroupDetail>();
        Assert.True(second.IsDefault);

        // The first group must have been demoted automatically.
        HttpResponseMessage firstAfterResponse = await client.GetAsync(
            $"/api/client-groups/{first.Id}"
        );
        ClientGroupDetail firstAfter =
            await firstAfterResponse.Content.ReadFromJsonAsync<ClientGroupDetail>();
        Assert.False(firstAfter.IsDefault);
    }

    [Fact]
    public async Task DeletingAClientGroup_RemovesItsOwnNetworksAndListAssignments()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage groupResponse = await client.PostAsJsonAsync(
            "/api/client-groups",
            new { name = $"cascade-b-{Guid.NewGuid():N}" }
        );
        ClientGroupDetail group =
            await groupResponse.Content.ReadFromJsonAsync<ClientGroupDetail>();

        HttpResponseMessage networkResponse = await client.PostAsJsonAsync(
            $"/api/client-groups/{group.Id}/networks",
            new { cidr = "203.0.113.0/24" }
        );
        ClientGroupNetworkDetail network =
            await networkResponse.Content.ReadFromJsonAsync<ClientGroupNetworkDetail>();

        HttpResponseMessage listResponse = await client.PostAsJsonAsync(
            "/api/filter-lists",
            new
            {
                name = $"cascade-b-list-{Guid.NewGuid():N}",
                kind = "Domain",
                action = "Block",
                source = "Local",
            }
        );
        FilterListDetail list = await listResponse.Content.ReadFromJsonAsync<FilterListDetail>();

        HttpResponseMessage assignmentResponse = await client.PostAsJsonAsync(
            $"/api/client-groups/{group.Id}/list-assignments",
            new { listId = list.Id }
        );
        ClientGroupListAssignmentDetail assignment =
            await assignmentResponse.Content.ReadFromJsonAsync<ClientGroupListAssignmentDetail>();

        // Deleting the group without first removing its network and assignment must succeed;
        // ClientGroupsController.Delete removes both.
        HttpResponseMessage deleteResponse = await client.DeleteAsync(
            $"/api/client-groups/{group.Id}"
        );
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        HttpResponseMessage networkAfterDeleteResponse = await client.GetAsync(
            $"/api/client-groups/{group.Id}/networks/{network.Id}"
        );
        Assert.Equal(HttpStatusCode.NotFound, networkAfterDeleteResponse.StatusCode);

        HttpResponseMessage assignmentAfterDeleteResponse = await client.GetAsync(
            $"/api/client-groups/{group.Id}/list-assignments/{assignment.Id}"
        );
        Assert.Equal(HttpStatusCode.NotFound, assignmentAfterDeleteResponse.StatusCode);
    }

    [Theory]
    [InlineData("/api/filter-lists")]
    [InlineData("/api/client-groups")]
    public async Task FilteringEndpoints_WithoutAToken_ReturnUnauthorized(string url)
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
