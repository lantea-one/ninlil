using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="SafeSearchController" /> over HTTP on the shared in-process
///     host, signed in as the bootstrap administrator. It checks that turning a catalog entry on
///     creates ordinary rewrites (visible through <see cref="DnsRewritesController" />) and
///     turning it off removes them, that turning one on writes the rewrite engine switch on in the
///     host's temporary settings file and turning it off never writes it back off, that a
///     conflicting existing rewrite is rejected with nothing written, and that a client or
///     client-group target creates rewrites scoped to that target only. Each test turns its
///     entries and the switch off again afterwards, since the database and settings file are
///     shared.
/// </summary>
[Collection(WebTestCollection.Name)]
public class SafeSearchControllerTests(NinlilDnsWebApplicationFactory factory)
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

    [Fact]
    public async Task List_ReturnsTheRealCatalogWithADuckDuckGoEntry()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync("/api/safe-search");
        response.EnsureSuccessStatusCode();

        IReadOnlyList<SafeSearchDetail> catalog = await response.Content.ReadFromJsonAsync<
            IReadOnlyList<SafeSearchDetail>
        >();

        SafeSearchDetail duckDuckGo = Assert.Single(catalog, entry => entry.Key == "duckduckgo");
        Assert.Equal(1, duckDuckGo.HostnameCount);
        Assert.False(duckDuckGo.IsEnabled);
    }

    [Fact]
    public async Task Enable_CreatesEveryMappingAndTurnsTheGlobalRewriteEngineOn()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        try
        {
            HttpResponseMessage enableResponse = await client.PostAsync(
                "/api/safe-search/duckduckgo",
                content: null
            );
            enableResponse.EnsureSuccessStatusCode();
            SafeSearchDetail enabled =
                await enableResponse.Content.ReadFromJsonAsync<SafeSearchDetail>();
            Assert.True(enabled.IsEnabled);

            // The global rewrite switch was turned on as a side effect (see SafeSearchController).
            // It is checked in the settings file, since a GET of /api/rewrite-settings would still
            // show the old value (see NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath).
            JsonNode rewriteSection = JsonNode
                .Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath))
                ?["DnsServer"]?["Rewrites"];
            Assert.NotNull(rewriteSection);
            Assert.True(rewriteSection["Enable"].GetValue<bool>());

            // The rewrite row exists and matches DuckDuckGo's published mapping.
            HttpResponseMessage rewritesResponse = await client.GetAsync(
                "/api/dns-rewrites?take=200"
            );
            rewritesResponse.EnsureSuccessStatusCode();
            PagedResult<DnsRewriteDetail> rewrites =
                await rewritesResponse.Content.ReadFromJsonAsync<PagedResult<DnsRewriteDetail>>();
            DnsRewriteDetail rewrite = Assert.Single(
                rewrites.Items,
                item => item.Domain == "duckduckgo.com"
            );
            Assert.Equal("safe.duckduckgo.com", rewrite.Target);
            Assert.False(rewrite.IsDisabled);

            // Enabling an enabled entry is idempotent: no duplicate row.
            HttpResponseMessage reEnableResponse = await client.PostAsync(
                "/api/safe-search/duckduckgo",
                content: null
            );
            reEnableResponse.EnsureSuccessStatusCode();

            HttpResponseMessage rewritesAfterReEnableResponse = await client.GetAsync(
                "/api/dns-rewrites?take=200"
            );
            PagedResult<DnsRewriteDetail> rewritesAfterReEnable =
                await rewritesAfterReEnableResponse.Content.ReadFromJsonAsync<
                    PagedResult<DnsRewriteDetail>
                >();
            Assert.Single(rewritesAfterReEnable.Items, item => item.Domain == "duckduckgo.com");

            // Disabling removes the row but leaves the global switch alone, so the operator's
            // other rewrites that rely on it keep working.
            HttpResponseMessage disableResponse = await client.DeleteAsync(
                "/api/safe-search/duckduckgo"
            );
            Assert.Equal(HttpStatusCode.NoContent, disableResponse.StatusCode);

            HttpResponseMessage rewritesAfterDisableResponse = await client.GetAsync(
                "/api/dns-rewrites?take=200"
            );
            PagedResult<DnsRewriteDetail> rewritesAfterDisable =
                await rewritesAfterDisableResponse.Content.ReadFromJsonAsync<
                    PagedResult<DnsRewriteDetail>
                >();
            Assert.DoesNotContain(
                rewritesAfterDisable.Items,
                item => item.Domain == "duckduckgo.com"
            );

            // Read from the file as above: disabling must not have written the switch back off.
            JsonNode rewriteSectionAfterDisable = JsonNode
                .Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath))
                ?["DnsServer"]?["Rewrites"];
            Assert.NotNull(rewriteSectionAfterDisable);
            Assert.True(rewriteSectionAfterDisable["Enable"].GetValue<bool>());
        }
        finally
        {
            // Turn the entry and the switch off again, since the database and settings file are
            // shared with every other test.
            await client.DeleteAsync("/api/safe-search/duckduckgo");
            await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Patch, "/api/rewrite-settings")
                {
                    Content = System.Net.Http.Json.JsonContent.Create(new { enable = false }),
                }
            );
        }
    }

    [Fact]
    public async Task Enable_WithAConflictingExistingRewrite_ReturnsConflictAndWritesNothing()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage conflictingCreateResponse = await client.PostAsJsonAsync(
            "/api/dns-rewrites",
            new { domain = "duckduckgo.com", target = "unrelated.example.com" }
        );
        conflictingCreateResponse.EnsureSuccessStatusCode();
        DnsRewriteDetail conflictingRewrite =
            await conflictingCreateResponse.Content.ReadFromJsonAsync<DnsRewriteDetail>();

        try
        {
            HttpResponseMessage enableResponse = await client.PostAsync(
                "/api/safe-search/duckduckgo",
                content: null
            );
            Assert.Equal(HttpStatusCode.Conflict, enableResponse.StatusCode);

            // Nothing was written -- DuckDuckGo must still report disabled.
            HttpResponseMessage listResponse = await client.GetAsync("/api/safe-search");
            IReadOnlyList<SafeSearchDetail> catalog = await listResponse.Content.ReadFromJsonAsync<
                IReadOnlyList<SafeSearchDetail>
            >();
            Assert.False(catalog.Single(entry => entry.Key == "duckduckgo").IsEnabled);
        }
        finally
        {
            await client.DeleteAsync($"/api/dns-rewrites/{conflictingRewrite.Id}");
        }
    }

    [Fact]
    public async Task SafeSearchEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/safe-search");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<Guid> CreateKnownClientAsync(HttpClient client, string suffix)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/clients",
            new { clientIdentifier = $"AA:BB:DD:{suffix}", protocolVersion = "IPv4" }
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

    /// <summary>
    ///     This test checks per-client scoping. Enabling Safe Search for one client creates rewrite
    ///     rows carrying that client's id (<see cref="DnsRewriteDetail.ClientId" />) next to the
    ///     global rows, the entry reads as enabled for that client but not for another, and
    ///     disabling it for that client removes only its rows and leaves the global ones.
    /// </summary>
    [Fact]
    public async Task Enable_WithAClientTarget_CreatesScopedRewrites_AndDisableLeavesOtherScopesUntouched()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        Guid firstClientId = await CreateKnownClientAsync(client, "000001");
        Guid secondClientId = await CreateKnownClientAsync(client, "000002");

        try
        {
            // A global "bing" Safe Search is already active for everyone.
            HttpResponseMessage globalEnableResponse = await client.PostAsync(
                "/api/safe-search/bing",
                content: null
            );
            globalEnableResponse.EnsureSuccessStatusCode();

            HttpResponseMessage enableResponse = await client.PostAsync(
                $"/api/safe-search/bing?clientId={firstClientId}",
                content: null
            );
            enableResponse.EnsureSuccessStatusCode();
            SafeSearchDetail enabledForFirstClient =
                await enableResponse.Content.ReadFromJsonAsync<SafeSearchDetail>();
            Assert.True(enabledForFirstClient.IsEnabled);

            // The second client has no scoped rows. The scoped view ignores the global rows, so it
            // reads as disabled even though bing is on globally.
            HttpResponseMessage secondClientListResponse = await client.GetAsync(
                $"/api/safe-search?clientId={secondClientId}"
            );
            IReadOnlyList<SafeSearchDetail> secondClientCatalog =
                await secondClientListResponse.Content.ReadFromJsonAsync<
                    IReadOnlyList<SafeSearchDetail>
                >();
            Assert.False(secondClientCatalog.Single(entry => entry.Key == "bing").IsEnabled);

            // The rows created carry the first client's id.
            HttpResponseMessage rewritesResponse = await client.GetAsync(
                "/api/dns-rewrites?take=200"
            );
            rewritesResponse.EnsureSuccessStatusCode();
            PagedResult<DnsRewriteDetail> rewrites =
                await rewritesResponse.Content.ReadFromJsonAsync<PagedResult<DnsRewriteDetail>>();
            List<DnsRewriteDetail> scopedRows =
            [
                .. rewrites.Items.Where(item =>
                    item.Domain is "www.bing.com" or "edgeservices.bing.com"
                    && item.ClientId == firstClientId
                ),
            ];
            Assert.Equal(2, scopedRows.Count);
            Assert.All(scopedRows, row => Assert.Null(row.ClientGroupId));

            // The global bing rows from the untargeted enable are still there and unscoped; a
            // targeted enable adds rows and replaces nothing.
            List<DnsRewriteDetail> globalRows =
            [
                .. rewrites.Items.Where(item =>
                    item.Domain is "www.bing.com" or "edgeservices.bing.com"
                    && item.ClientId == null
                    && item.ClientGroupId == null
                ),
            ];
            Assert.Equal(2, globalRows.Count);

            // Disabling for the first client removes only its scoped rows.
            HttpResponseMessage disableForFirstResponse = await client.DeleteAsync(
                $"/api/safe-search/bing?clientId={firstClientId}"
            );
            Assert.Equal(HttpStatusCode.NoContent, disableForFirstResponse.StatusCode);

            HttpResponseMessage rewritesAfterDisableResponse = await client.GetAsync(
                "/api/dns-rewrites?take=200"
            );
            PagedResult<DnsRewriteDetail> rewritesAfterDisable =
                await rewritesAfterDisableResponse.Content.ReadFromJsonAsync<
                    PagedResult<DnsRewriteDetail>
                >();
            Assert.DoesNotContain(
                rewritesAfterDisable.Items,
                item => item.ClientId == firstClientId
            );
            // The global rows must still be there.
            Assert.Equal(
                2,
                rewritesAfterDisable.Items.Count(item =>
                    item.Domain is "www.bing.com" or "edgeservices.bing.com"
                    && item.ClientId == null
                    && item.ClientGroupId == null
                )
            );
        }
        finally
        {
            await client.DeleteAsync($"/api/safe-search/bing?clientId={firstClientId}");
            await client.DeleteAsync($"/api/safe-search/bing?clientId={secondClientId}");
            await client.DeleteAsync("/api/safe-search/bing");
            await client.DeleteAsync($"/api/clients/{firstClientId}");
            await client.DeleteAsync($"/api/clients/{secondClientId}");
            await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Patch, "/api/rewrite-settings")
                {
                    Content = JsonContent.Create(new { enable = false }),
                }
            );
        }
    }

    /// <summary>
    ///     This test checks that a client-group target creates group-scoped rewrites, and that
    ///     naming both a client and a group is rejected.
    /// </summary>
    [Fact]
    public async Task Enable_WithAClientGroupTarget_CreatesGroupScopedRewrites_AndBothTargetsAtOnceIsRejected()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        Guid groupId = await CreateClientGroupAsync(client);

        try
        {
            HttpResponseMessage bothTargetsResponse = await client.PostAsync(
                $"/api/safe-search/google?clientId={Guid.NewGuid()}&clientGroupId={groupId}",
                content: null
            );
            Assert.Equal(HttpStatusCode.BadRequest, bothTargetsResponse.StatusCode);

            HttpResponseMessage enableForGroupResponse = await client.PostAsync(
                $"/api/safe-search/google?clientGroupId={groupId}",
                content: null
            );
            enableForGroupResponse.EnsureSuccessStatusCode();
            SafeSearchDetail enabledForGroup =
                await enableForGroupResponse.Content.ReadFromJsonAsync<SafeSearchDetail>();
            Assert.True(enabledForGroup.IsEnabled);

            HttpResponseMessage rewritesResponse = await client.GetAsync(
                "/api/dns-rewrites?take=200"
            );
            PagedResult<DnsRewriteDetail> rewrites =
                await rewritesResponse.Content.ReadFromJsonAsync<PagedResult<DnsRewriteDetail>>();
            Assert.Contains(
                rewrites.Items,
                item => item.Domain == "www.google.com" && item.ClientGroupId == groupId
            );
        }
        finally
        {
            await client.DeleteAsync($"/api/safe-search/google?clientGroupId={groupId}");
            await client.DeleteAsync($"/api/client-groups/{groupId}");
            await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Patch, "/api/rewrite-settings")
                {
                    Content = JsonContent.Create(new { enable = false }),
                }
            );
        }
    }

    [Fact]
    public async Task Enable_WithAnUnknownClientTarget_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsync(
            $"/api/safe-search/duckduckgo?clientId={Guid.NewGuid()}",
            content: null
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
