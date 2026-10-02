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
///     This class covers the forwarder endpoints of <see cref="ForwardersController" /> over HTTP
///     on the shared in-process host, signed in as the bootstrap administrator: the create, list,
///     get, replace, patch and delete lifecycle, validation of the definition string by
///     <c>IUpstreamForwarderDefinitionParser</c> (a garbage string, an empty one, and an
///     <c>sdns://</c> DNSCrypt stamp built by <see cref="BuildPlainDnsCryptStamp" /> so
///     <c>DnsCryptStampParser</c> is exercised too), and rejection without a token. No query is
///     forwarded; forwarding is off in the test host.
/// </summary>
[Collection(WebTestCollection.Name)]
public class ForwarderCrudTests(NinlilDnsWebApplicationFactory factory)
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

    /// <summary>
    ///     This method builds an <c>sdns://</c> DNSCrypt stamp (protocol identifier <c>0x01</c>) by
    ///     hand, independently of <c>DnsCryptStampParser</c>, so the test checks the parser rather
    ///     than its own encoding. Layout: <c>0x01 || props (8 zero bytes) || LP(addr) ||
    ///     LP(32-byte public key) || LP(providerName)</c>, base64url-encoded without padding, as in
    ///     the stamps specification <c>DnsCryptStampParser</c> cites.
    /// </summary>
    private static string BuildPlainDnsCryptStamp()
    {
        List<byte> bytes = [0x01];
        bytes.AddRange(new byte[8]); // props, unused by the parser beyond skipping past it.

        void AddLengthPrefixed(byte[] field)
        {
            bytes.Add((byte)field.Length);
            bytes.AddRange(field);
        }

        AddLengthPrefixed(Encoding.UTF8.GetBytes("203.0.113.5:443"));
        AddLengthPrefixed(Enumerable.Repeat((byte)0x11, 32).ToArray());
        AddLengthPrefixed(Encoding.UTF8.GetBytes("2.dnscrypt-cert.example.com"));

        string base64 = Convert.ToBase64String([.. bytes]);
        string base64Url = base64.Replace('+', '-').Replace('/', '_').TrimEnd('=');

        return $"sdns://{base64Url}";
    }

    [Fact]
    public async Task ForwarderLifecycle_CreateListGetReplacePatchAndDelete_WorksEndToEnd()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string name = $"forwarder-{Guid.NewGuid():N}";

        // Creating a plain, bare-address forwarder.
        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/forwarders",
            new
            {
                name,
                definitionString = "203.0.113.53",
                sortOrder = 5,
                comments = "created by test",
            }
        );
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        ForwarderDetail created = await createResponse.Content.ReadFromJsonAsync<ForwarderDetail>();
        Assert.Equal(name, created.Name);
        Assert.Equal("203.0.113.53", created.DefinitionString);
        Assert.Equal(UpstreamForwarderProtocolEnum.Plain, created.Protocol);
        Assert.Equal(5, created.SortOrder);
        Assert.False(created.IsDisabled);
        Assert.NotNull(created.OriginatingNodeId);

        // Duplicate name must be rejected.
        HttpResponseMessage duplicateResponse = await client.PostAsJsonAsync(
            "/api/forwarders",
            new { name, definitionString = "203.0.113.54" }
        );
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);

        // Listing reflects it, with pagination fields present.
        HttpResponseMessage listResponse = await client.GetAsync("/api/forwarders");
        listResponse.EnsureSuccessStatusCode();
        PagedResult<ForwarderDetail> page = await listResponse.Content.ReadFromJsonAsync<
            PagedResult<ForwarderDetail>
        >();
        Assert.Contains(page.Items, item => item.Id == created.Id);
        Assert.True(page.Take > 0);

        // Getting it by identifier.
        HttpResponseMessage getResponse = await client.GetAsync($"/api/forwarders/{created.Id}");
        getResponse.EnsureSuccessStatusCode();
        ForwarderDetail fetched = await getResponse.Content.ReadFromJsonAsync<ForwarderDetail>();
        Assert.Equal(created.Id, fetched.Id);

        // PUT replaces the definition string and sort order and keeps the identifier. A literal
        // IP address is used so the parse needs no DNS lookup (ParseHostPort skips ResolveHost
        // for a literal), keeping the test free of external services.
        HttpResponseMessage replaceResponse = await client.PutAsJsonAsync(
            $"/api/forwarders/{created.Id}",
            new
            {
                name,
                definitionString = "tls://203.0.113.10:853",
                sortOrder = 1,
                isDisabled = true,
            }
        );
        Assert.Equal(HttpStatusCode.NoContent, replaceResponse.StatusCode);

        HttpResponseMessage afterReplaceResponse = await client.GetAsync(
            $"/api/forwarders/{created.Id}"
        );
        ForwarderDetail replaced =
            await afterReplaceResponse.Content.ReadFromJsonAsync<ForwarderDetail>();
        Assert.Equal(created.Id, replaced.Id);
        Assert.Equal("tls://203.0.113.10:853", replaced.DefinitionString);
        Assert.Equal(UpstreamForwarderProtocolEnum.Tls, replaced.Protocol);
        Assert.Equal(1, replaced.SortOrder);
        Assert.True(replaced.IsDisabled);

        // PATCH changes only the comments.
        HttpResponseMessage patchResponse = await client.SendAsync(
            PatchRequest($"/api/forwarders/{created.Id}", new { comments = "patched" })
        );
        Assert.Equal(HttpStatusCode.NoContent, patchResponse.StatusCode);

        HttpResponseMessage afterPatchResponse = await client.GetAsync(
            $"/api/forwarders/{created.Id}"
        );
        ForwarderDetail patched =
            await afterPatchResponse.Content.ReadFromJsonAsync<ForwarderDetail>();
        Assert.Equal("patched", patched.Comments);
        Assert.Equal("tls://203.0.113.10:853", patched.DefinitionString);
        Assert.True(patched.IsDisabled);

        // Delete it.
        HttpResponseMessage deleteResponse = await client.DeleteAsync(
            $"/api/forwarders/{created.Id}"
        );
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        HttpResponseMessage afterDeleteResponse = await client.GetAsync(
            $"/api/forwarders/{created.Id}"
        );
        Assert.Equal(HttpStatusCode.NotFound, afterDeleteResponse.StatusCode);
    }

    [Fact]
    public async Task Create_WithGarbageDefinitionString_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/forwarders",
            new
            {
                name = $"garbage-{Guid.NewGuid():N}",
                definitionString = "not a real forwarder definition at all!!",
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithEmptyDefinitionString_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/forwarders",
            new { name = $"empty-{Guid.NewGuid():N}", definitionString = "" }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithAValidSdnsDnsCryptStamp_IsAcceptedAndParsedAsDnsCrypt()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string stamp = BuildPlainDnsCryptStamp();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/forwarders",
            new { name = $"dnscrypt-{Guid.NewGuid():N}", definitionString = stamp }
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        ForwarderDetail created = await response.Content.ReadFromJsonAsync<ForwarderDetail>();
        Assert.Equal(stamp, created.DefinitionString);
        Assert.Equal(UpstreamForwarderProtocolEnum.DnsCrypt, created.Protocol);
    }

    [Fact]
    public async Task ForwardersEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/forwarders");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
