using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Tsig;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the TSIG key endpoints over HTTP on the shared in-process host, signed in
///     as the bootstrap administrator: creating a key with a generated or a supplied secret, the
///     secret being returned once on creation and left out of every later response, editing with
///     PUT and PATCH, deleting, rejecting an unsupported algorithm or an invalid name, storing
///     names in lowercase, the four algorithms, and the rules for a cluster key (listed with
///     <c>isClusterKey</c>, no rename or delete, still disableable). Cluster keys are saved
///     directly through the repository, since a full cluster join needs two processes.
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

        // With no secret supplied, a random one is generated and returned this once.
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

        // GET never returns the secret.
        HttpResponseMessage getResponse = await client.GetAsync($"/api/tsig-keys/{created.Key.Id}");
        getResponse.EnsureSuccessStatusCode();
        string getBody = await getResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(created.Secret, getBody, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", getBody, StringComparison.OrdinalIgnoreCase);

        // The list must not include the secret either.
        HttpResponseMessage listResponse = await client.GetAsync("/api/tsig-keys");
        listResponse.EnsureSuccessStatusCode();
        string listBody = await listResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(created.Secret, listBody, StringComparison.Ordinal);

        // PUT replaces the key's metadata and keeps its identifier.
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
        // The fields the patch did not name must be unchanged.
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

    /// <summary>
    ///     This method saves a key that clustering would own: linked to a peer, or an unused invite
    ///     key.
    /// </summary>
    private async Task<TsigKeyEntity> SeedClusterKeyAsync(bool unusedInvite)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ITsigKeyRepository repository =
            scope.ServiceProvider.GetRequiredService<ITsigKeyRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        TsigKeyEntity key = new()
        {
            Id = Guid.NewGuid(),
            Name = $"cluster-{Guid.NewGuid():N}",
            AlgorithmName = "hmac-sha256",
            SecretEncrypted = scope
                .ServiceProvider.GetRequiredService<TsigSecretProtector>()
                .Protect("a-shared-cluster-channel-secret!"u8.ToArray()),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ClusterNodeId = unusedInvite ? null : Guid.NewGuid(),
            ClusterInviteExpiresAtUtc = unusedInvite ? DateTimeOffset.UtcNow.AddHours(1) : null,
        };
        await repository.AddAsync(key);
        await unitOfWork.SaveChangesAsync();

        return key;
    }

    [Fact]
    public async Task CreateTsigKey_WithAMixedCaseName_StoresAndListsItLowercase()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string suffix = Guid.NewGuid().ToString("N");

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/tsig-keys",
            new { name = $"Ops.Key-{suffix}.Example.COM." }
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        TsigKeyCreateResponse created =
            await response.Content.ReadFromJsonAsync<TsigKeyCreateResponse>();
        Assert.Equal($"ops.key-{suffix}.example.com", created.Key.Name);
        Assert.False(created.Key.IsClusterKey);

        TsigKeyDetail fetched = await client.GetFromJsonAsync<TsigKeyDetail>(
            $"/api/tsig-keys/{created.Key.Id}"
        );
        Assert.Equal($"ops.key-{suffix}.example.com", fetched.Name);

        // The same name in another case is the same key.
        HttpResponseMessage duplicate = await client.PostAsJsonAsync(
            "/api/tsig-keys",
            new { name = $"OPS.KEY-{suffix}.example.com" }
        );
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Theory]
    [InlineData("hmac-sha1", 20)]
    [InlineData("HMAC-SHA256.", 32)]
    [InlineData("hmac-sha384", 48)]
    [InlineData("hmac-sha512", 64)]
    public async Task CreateTsigKey_WithASupportedAlgorithm_StoresItsCanonicalNameAndSizesTheSecret(
        string requested,
        int expectedSecretBytes
    )
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/tsig-keys",
            new { name = $"algo-{Guid.NewGuid():N}.example.com", algorithmName = requested }
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        TsigKeyCreateResponse created =
            await response.Content.ReadFromJsonAsync<TsigKeyCreateResponse>();

        Assert.Equal(requested.TrimEnd('.').ToLowerInvariant(), created.Key.AlgorithmName);
        Assert.Equal(expectedSecretBytes, Convert.FromBase64String(created.Secret).Length);
    }

    [Theory]
    [InlineData("hmac-md5")]
    [InlineData("hmac-sha224")]
    [InlineData("hmac-sha256-128")]
    public async Task CreateTsigKey_WithAnUnsupportedAlgorithm_Returns400NamingTheSupportedOnes(
        string requested
    )
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/tsig-keys",
            new { name = $"algo-{Guid.NewGuid():N}.example.com", algorithmName = requested }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("hmac-sha512", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplaceTsigKey_ToAnotherSupportedAlgorithm_StoresIt()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/tsig-keys",
            new { name = $"switch-{Guid.NewGuid():N}.example.com" }
        );
        TsigKeyCreateResponse created =
            await createResponse.Content.ReadFromJsonAsync<TsigKeyCreateResponse>();

        HttpResponseMessage replace = await client.PutAsJsonAsync(
            $"/api/tsig-keys/{created.Key.Id}",
            new { name = created.Key.Name, algorithmName = "hmac-sha512" }
        );
        Assert.Equal(HttpStatusCode.NoContent, replace.StatusCode);

        TsigKeyDetail fetched = await client.GetFromJsonAsync<TsigKeyDetail>(
            $"/api/tsig-keys/{created.Key.Id}"
        );
        Assert.Equal("hmac-sha512", fetched.AlgorithmName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClusterKey_IsListedAsAClusterKey_AndCannotBeRenamedOrDeleted(
        bool unusedInvite
    )
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        TsigKeyEntity key = await SeedClusterKeyAsync(unusedInvite);

        TsigKeyDetail fetched = await client.GetFromJsonAsync<TsigKeyDetail>(
            $"/api/tsig-keys/{key.Id}"
        );
        Assert.True(fetched.IsClusterKey);

        HttpResponseMessage rename = await client.SendAsync(
            PatchRequest($"/api/tsig-keys/{key.Id}", new { name = $"renamed-{key.Name}" })
        );
        Assert.Equal(HttpStatusCode.Conflict, rename.StatusCode);

        HttpResponseMessage replace = await client.PutAsJsonAsync(
            $"/api/tsig-keys/{key.Id}",
            new { name = $"renamed-{key.Name}", algorithmName = "hmac-sha256" }
        );
        Assert.Equal(HttpStatusCode.Conflict, replace.StatusCode);

        HttpResponseMessage delete = await client.DeleteAsync($"/api/tsig-keys/{key.Id}");
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);

        // Still there, unchanged.
        TsigKeyDetail after = await client.GetFromJsonAsync<TsigKeyDetail>(
            $"/api/tsig-keys/{key.Id}"
        );
        Assert.Equal(key.Name, after.Name);
    }

    [Fact]
    public async Task ClusterKey_CanBeDisabledAndCommented_ThroughPatchAndPut()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        TsigKeyEntity key = await SeedClusterKeyAsync(unusedInvite: false);

        HttpResponseMessage patch = await client.SendAsync(
            PatchRequest($"/api/tsig-keys/{key.Id}", new { isDisabled = true })
        );
        Assert.Equal(HttpStatusCode.NoContent, patch.StatusCode);

        HttpResponseMessage replace = await client.PutAsJsonAsync(
            $"/api/tsig-keys/{key.Id}",
            new
            {
                name = key.Name,
                algorithmName = "hmac-sha256",
                isDisabled = true,
                comments = "emergency switch",
            }
        );
        Assert.Equal(HttpStatusCode.NoContent, replace.StatusCode);

        TsigKeyDetail after = await client.GetFromJsonAsync<TsigKeyDetail>(
            $"/api/tsig-keys/{key.Id}"
        );
        Assert.True(after.IsDisabled);
        Assert.True(after.IsClusterKey);
        Assert.Equal("emergency switch", after.Comments);
    }
}
