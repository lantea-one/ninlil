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
///     This class covers the DNS rewrite endpoints of <see cref="DnsRewritesController" /> over
///     HTTP on the shared in-process host, signed in as the bootstrap administrator: the create,
///     list, get, replace, patch and delete lifecycle, validation of the domain and target (with
///     the target kind worked out by <c>DnsRewriteTargetClassifier</c>), rejection of a second
///     rewrite with the same domain and record kind, and rejection without a token. How a
///     rewrite answers a query is covered by <c>Ninlil.Dns.Server.Rewrites.Tests.DnsRewriteEngineTests</c>.
/// </summary>
[Collection(WebTestCollection.Name)]
public class DnsRewriteCrudTests(NinlilDnsWebApplicationFactory factory)
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
    public async Task RewriteLifecycle_CreateListGetReplacePatchAndDelete_WorksEndToEnd()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string domain = $"host-{Guid.NewGuid():N}.example.com";

        // Creating an A-target rewrite.
        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/dns-rewrites",
            new
            {
                domain,
                target = "203.0.113.5",
                comments = "created by test",
            }
        );
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        DnsRewriteDetail created =
            await createResponse.Content.ReadFromJsonAsync<DnsRewriteDetail>();
        Assert.Equal(domain, created.Domain);
        Assert.Equal("203.0.113.5", created.Target);
        Assert.Equal(DnsRewriteTargetKindEnum.A, created.TargetKind);
        Assert.False(created.IsDisabled);
        Assert.NotNull(created.OriginatingNodeId);

        // A second A-target rewrite for the same domain is ambiguous and must be rejected.
        HttpResponseMessage duplicateResponse = await client.PostAsJsonAsync(
            "/api/dns-rewrites",
            new { domain, target = "203.0.113.6" }
        );
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);

        // An AAAA-target rewrite for the same domain is fine, since the record kind differs.
        HttpResponseMessage aaaaResponse = await client.PostAsJsonAsync(
            "/api/dns-rewrites",
            new { domain, target = "2001:db8::1" }
        );
        Assert.Equal(HttpStatusCode.Created, aaaaResponse.StatusCode);
        DnsRewriteDetail aaaaCreated =
            await aaaaResponse.Content.ReadFromJsonAsync<DnsRewriteDetail>();
        Assert.Equal(DnsRewriteTargetKindEnum.Aaaa, aaaaCreated.TargetKind);

        // Listing reflects both, with pagination fields present.
        HttpResponseMessage listResponse = await client.GetAsync("/api/dns-rewrites");
        listResponse.EnsureSuccessStatusCode();
        PagedResult<DnsRewriteDetail> page = await listResponse.Content.ReadFromJsonAsync<
            PagedResult<DnsRewriteDetail>
        >();
        Assert.Contains(page.Items, item => item.Id == created.Id);
        Assert.Contains(page.Items, item => item.Id == aaaaCreated.Id);
        Assert.True(page.Take > 0);

        // Getting it by identifier.
        HttpResponseMessage getResponse = await client.GetAsync($"/api/dns-rewrites/{created.Id}");
        getResponse.EnsureSuccessStatusCode();
        DnsRewriteDetail fetched = await getResponse.Content.ReadFromJsonAsync<DnsRewriteDetail>();
        Assert.Equal(created.Id, fetched.Id);

        // PUT replaces the target, turning this into a CNAME-style rewrite, and keeps the
        // identifier.
        HttpResponseMessage replaceResponse = await client.PutAsJsonAsync(
            $"/api/dns-rewrites/{created.Id}",
            new
            {
                domain,
                target = "canonical.example.net",
                isDisabled = true,
            }
        );
        Assert.Equal(HttpStatusCode.NoContent, replaceResponse.StatusCode);

        HttpResponseMessage afterReplaceResponse = await client.GetAsync(
            $"/api/dns-rewrites/{created.Id}"
        );
        DnsRewriteDetail replaced =
            await afterReplaceResponse.Content.ReadFromJsonAsync<DnsRewriteDetail>();
        Assert.Equal(created.Id, replaced.Id);
        Assert.Equal("canonical.example.net", replaced.Target);
        Assert.Equal(DnsRewriteTargetKindEnum.Cname, replaced.TargetKind);
        Assert.True(replaced.IsDisabled);

        // PATCH partially updates just the comments, leaving everything else untouched.
        HttpResponseMessage patchResponse = await client.SendAsync(
            PatchRequest($"/api/dns-rewrites/{created.Id}", new { comments = "patched" })
        );
        Assert.Equal(HttpStatusCode.NoContent, patchResponse.StatusCode);

        HttpResponseMessage afterPatchResponse = await client.GetAsync(
            $"/api/dns-rewrites/{created.Id}"
        );
        DnsRewriteDetail patched =
            await afterPatchResponse.Content.ReadFromJsonAsync<DnsRewriteDetail>();
        Assert.Equal("patched", patched.Comments);
        Assert.Equal("canonical.example.net", patched.Target);
        Assert.True(patched.IsDisabled);

        // Delete both.
        HttpResponseMessage deleteResponse = await client.DeleteAsync(
            $"/api/dns-rewrites/{created.Id}"
        );
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        await client.DeleteAsync($"/api/dns-rewrites/{aaaaCreated.Id}");

        HttpResponseMessage afterDeleteResponse = await client.GetAsync(
            $"/api/dns-rewrites/{created.Id}"
        );
        Assert.Equal(HttpStatusCode.NotFound, afterDeleteResponse.StatusCode);
    }

    [Fact]
    public async Task Create_WithAWildcardDomain_IsAccepted()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/dns-rewrites",
            new { domain = $"*.wild-{Guid.NewGuid():N}.example.com", target = "203.0.113.7" }
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithAnInvalidDomain_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/dns-rewrites",
            new { domain = new string('a', 300), target = "203.0.113.7" }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithAnEmptyDomain_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/dns-rewrites",
            new { domain = "", target = "203.0.113.7" }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithAnEmptyTarget_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/dns-rewrites",
            new { domain = $"empty-{Guid.NewGuid():N}.example.com", target = "" }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithADomainTargetForACnameStyleRewrite_IsAcceptedAndClassifiedAsCname()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/dns-rewrites",
            new
            {
                domain = $"alias-{Guid.NewGuid():N}.example.com",
                target = "canonical.example.net",
            }
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        DnsRewriteDetail created = await response.Content.ReadFromJsonAsync<DnsRewriteDetail>();
        Assert.Equal(DnsRewriteTargetKindEnum.Cname, created.TargetKind);
    }

    [Fact]
    public async Task RewritesEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/dns-rewrites");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
