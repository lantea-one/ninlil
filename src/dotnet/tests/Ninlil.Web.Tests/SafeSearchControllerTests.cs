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
///     This class covers <see cref="SafeSearchController" /> end to end, against a real,
///     in-process host, a real SQLite database, and an isolated, throwaway JSON configuration
///     file (see <see cref="NinlilDnsWebApplicationFactory" />) -- confirming a catalog toggle
///     really does create/remove ordinary <see cref="DnsRewritesController" /> rows, that enabling
///     an entry turns the global rewrite engine on when it was off, that disabling never turns it
///     back off, and that a genuine hostname conflict is rejected with nothing at all written.
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

            // The single global rewrite toggle turned on as a side effect -- documented behavior,
            // see SafeSearchController's own doc comment. Confirmed by reading the isolated
            // runtime configuration file directly, not by re-GETting /api/rewrite-settings -- this
            // test host's own isolated runtime-settings file is never registered as its own
            // reload-on-change IConfiguration source (a known WebApplicationFactory limitation
            // this project's settings-controller test suite already documented), so a live re-read can't be trusted
            // to reflect a just-written value; ClientSubnetSettingsControllerTests establishes the
            // same direct-file-read workaround for the identical reason.
            JsonNode rewriteSection = JsonNode
                .Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath))
                ?["DnsServer"]?["Rewrites"];
            Assert.NotNull(rewriteSection);
            Assert.True(rewriteSection["Enable"].GetValue<bool>());

            // The real rewrite row exists, exactly matching DuckDuckGo's own official mapping.
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

            // Enabling an already-enabled entry is idempotent -- no duplicate row.
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

            // Disabling removes the row but leaves the global toggle exactly as it was --
            // documented, deliberate: it must never silently break an operator's own unrelated
            // rewrites that might depend on it staying on.
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

            // Same direct-file-read workaround as above -- confirms disabling never wrote the
            // toggle back off (it was true from the earlier Enable call, and nothing here should
            // have changed it).
            JsonNode rewriteSectionAfterDisable = JsonNode
                .Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath))
                ?["DnsServer"]?["Rewrites"];
            Assert.NotNull(rewriteSectionAfterDisable);
            Assert.True(rewriteSectionAfterDisable["Enable"].GetValue<bool>());
        }
        finally
        {
            // Restore before returning -- shared database and configuration file across
            // WebTestCollection, the same discipline RewriteSettingsControllerTests already
            // establishes.
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
    ///     This test proves the real per-client scoping Phase 15e adds: enabling Safe Search for
    ///     one specific client creates rewrite rows carrying that client's own id (confirmed
    ///     directly via <see cref="DnsRewriteDetail.ClientId" />, not merely inferred from
    ///     behavior), reports enabled only for that client (not globally, and not for an unrelated
    ///     client), and disabling it for that client removes only its own scoped rows, leaving
    ///     every global row -- and any other target's own rows -- completely untouched.
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

            // The second, unrelated client has no scoped rows of its own -- it is unaffected by
            // the first client's own targeted enable, and unaffected by the global one too
            // (scoping is purely additive: this assertion only concerns the scoped view).
            HttpResponseMessage secondClientListResponse = await client.GetAsync(
                $"/api/safe-search?clientId={secondClientId}"
            );
            IReadOnlyList<SafeSearchDetail> secondClientCatalog =
                await secondClientListResponse.Content.ReadFromJsonAsync<
                    IReadOnlyList<SafeSearchDetail>
                >();
            Assert.False(secondClientCatalog.Single(entry => entry.Key == "bing").IsEnabled);

            // The real rewrite rows this created genuinely carry the first client's own id.
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

            // The pre-existing global "bing" rows (created by the very first, untargeted Enable
            // above) are still present and still unscoped -- a targeted enable is additive, never
            // a replacement for the global rows.
            List<DnsRewriteDetail> globalRows =
            [
                .. rewrites.Items.Where(item =>
                    item.Domain is "www.bing.com" or "edgeservices.bing.com"
                    && item.ClientId == null
                    && item.ClientGroupId == null
                ),
            ];
            Assert.Equal(2, globalRows.Count);

            // Disabling for the first client only removes its own scoped rows.
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
            // The global rows must still be there, completely untouched by the targeted disable.
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
    ///     This test proves the same scoping contract for a client-group target, and that naming
    ///     both a client and a group at once is rejected.
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
