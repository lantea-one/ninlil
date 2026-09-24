using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the TSIG key CRUD REST endpoints end-to-end, against a real,
///     in-process host and a real SQLite database (see <see cref="NinlilDnsWebApplicationFactory" />)
///     -- creating a key both with a server-generated and a caller-supplied secret, confirming the
///     secret is revealed exactly once (on creation) and omitted from every later response, editing
///     via both PUT and PATCH, and deleting.
/// </summary>
[Collection(WebTestCollection.Name)]
public class TsigKeyCrudTests(NinlilDnsWebApplicationFactory factory)
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
    public async Task TsigKeyLifecycle_CreateEditAndDelete_WorksEndToEnd()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string keyName = $"crud-{Guid.NewGuid():N}.example.com";

        // Creating with no secret supplied generates a random one, revealed exactly once.
        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/tsig-keys",
            new { name = keyName, comments = "created by test" }
        );
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        TsigKeyCreateResponse created =
            await createResponse.Content.ReadFromJsonAsync<TsigKeyCreateResponse>();
        Assert.Equal(keyName, created.Key.Name);
        Assert.False(string.IsNullOrWhiteSpace(created.Secret));
        Assert.True(Convert.FromBase64String(created.Secret).Length > 0);

        // Creating a second key with the same name must be rejected.
        HttpResponseMessage duplicateResponse = await client.PostAsJsonAsync(
            "/api/tsig-keys",
            new { name = keyName }
        );
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);

        // The key's own secret is never returned again, from GET.
        HttpResponseMessage getResponse = await client.GetAsync($"/api/tsig-keys/{created.Key.Id}");
        getResponse.EnsureSuccessStatusCode();
        string getBody = await getResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(created.Secret, getBody, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", getBody, StringComparison.OrdinalIgnoreCase);

        // Listing must show the key, and must not leak its secret either.
        HttpResponseMessage listResponse = await client.GetAsync("/api/tsig-keys");
        listResponse.EnsureSuccessStatusCode();
        string listBody = await listResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(created.Secret, listBody, StringComparison.Ordinal);

        // PUT fully replaces the key's own metadata, keeping its own identifier.
        string renamedKeyName = $"renamed-{Guid.NewGuid():N}.example.com";
        HttpResponseMessage replaceResponse = await client.PutAsJsonAsync(
            $"/api/tsig-keys/{created.Key.Id}",
            new
            {
                name = renamedKeyName,
                algorithmName = created.Key.AlgorithmName,
                isDisabled = true,
                comments = "replaced via PUT",
            }
        );
        Assert.Equal(HttpStatusCode.NoContent, replaceResponse.StatusCode);

        HttpResponseMessage afterReplaceResponse = await client.GetAsync(
            $"/api/tsig-keys/{created.Key.Id}"
        );
        TsigKeyDetail replaced =
            await afterReplaceResponse.Content.ReadFromJsonAsync<TsigKeyDetail>();
        Assert.Equal(created.Key.Id, replaced.Id);
        Assert.Equal(renamedKeyName, replaced.Name);
        Assert.True(replaced.IsDisabled);
        Assert.Equal("replaced via PUT", replaced.Comments);

        // PATCH via JSON Merge Patch only touches the field it names.
        HttpResponseMessage patchResponse = await client.SendAsync(
            PatchRequest($"/api/tsig-keys/{created.Key.Id}", new { isDisabled = false })
        );
        Assert.Equal(HttpStatusCode.NoContent, patchResponse.StatusCode);

        HttpResponseMessage afterPatchResponse = await client.GetAsync(
            $"/api/tsig-keys/{created.Key.Id}"
        );
        TsigKeyDetail patched = await afterPatchResponse.Content.ReadFromJsonAsync<TsigKeyDetail>();
        Assert.False(patched.IsDisabled);
        // The field the patch never mentioned must survive untouched.
        Assert.Equal(renamedKeyName, patched.Name);
        Assert.Equal("replaced via PUT", patched.Comments);

        // Deleting works and is reflected immediately.
        HttpResponseMessage deleteResponse = await client.DeleteAsync(
            $"/api/tsig-keys/{created.Key.Id}"
        );
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        HttpResponseMessage afterDeleteResponse = await client.GetAsync(
            $"/api/tsig-keys/{created.Key.Id}"
        );
        Assert.Equal(HttpStatusCode.NotFound, afterDeleteResponse.StatusCode);
    }

    [Fact]
    public async Task CreateTsigKey_WithACallerSuppliedSecret_ReturnsThatExactSecret()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string keyName = $"supplied-{Guid.NewGuid():N}.example.com";
        string suppliedSecret = Convert.ToBase64String("a-known-32-byte-shared-secret!!!"u8);

        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/tsig-keys",
            new { name = keyName, secret = suppliedSecret }
        );

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        TsigKeyCreateResponse created =
            await createResponse.Content.ReadFromJsonAsync<TsigKeyCreateResponse>();
        Assert.Equal(suppliedSecret, created.Secret);
    }

    [Fact]
    public async Task CreateTsigKey_WithAnUnsupportedAlgorithm_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/tsig-keys",
            new { name = $"bad-alg-{Guid.NewGuid():N}.example.com", algorithmName = "hmac-md5" }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateTsigKey_WithAnInvalidName_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string invalidName = $"{new string('a', 64)}.example.com";

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/tsig-keys",
            new { name = invalidName }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TsigKeyEndpoints_WithoutAToken_ReturnUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/tsig-keys");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
