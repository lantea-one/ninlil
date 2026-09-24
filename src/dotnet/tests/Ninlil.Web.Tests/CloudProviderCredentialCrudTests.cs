using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the cloud DNS provider credential CRUD REST endpoints end to
///     end, against a real, in-process host and a real SQLite database (see
///     <see cref="NinlilDnsWebApplicationFactory" />) -- creating a credential set for each
///     supported provider shape, confirming its own secret material is revealed exactly once (on
///     creation) and omitted from every later response, rotating that secret material via
///     <c>PUT</c> (this entity type's own deliberate deviation from <c>TsigKeysController</c>'s
///     secret-immutability stance -- see <see cref="Models.CloudProviderCredentialWriteRequest" />),
///     editing metadata-only via <c>PATCH</c>, and deleting.
/// </summary>
[Collection(WebTestCollection.Name)]
public class CloudProviderCredentialCrudTests(NinlilDnsWebApplicationFactory factory)
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
    public async Task CloudProviderCredentialLifecycle_CreateRotateAndDelete_WorksEndToEnd()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string name = $"crud-{Guid.NewGuid():N}";
        const string originalToken = "cf-original-token-value";

        // Creating a Cloudflare-shaped credential set reveals its own secret material exactly
        // once, in this response only.
        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/cloud-provider-credentials",
            new
            {
                name,
                providerType = "Cloudflare",
                credentials = new Dictionary<string, string> { ["ApiToken"] = originalToken },
                comments = "created by test",
            }
        );
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        CloudProviderCredentialCreateResponse created =
            await createResponse.Content.ReadFromJsonAsync<CloudProviderCredentialCreateResponse>();
        Assert.Equal(name, created.Credential.Name);
        Assert.Equal(CloudDnsProviderTypeEnum.Cloudflare, created.Credential.ProviderType);
        Assert.Equal(originalToken, created.Secrets["ApiToken"]);

        // Creating a second credential set with the same name must be rejected.
        HttpResponseMessage duplicateResponse = await client.PostAsJsonAsync(
            "/api/cloud-provider-credentials",
            new
            {
                name,
                providerType = "Cloudflare",
                credentials = new Dictionary<string, string> { ["ApiToken"] = "irrelevant" },
            }
        );
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);

        // The credential set's own secret material is never returned again, from GET.
        HttpResponseMessage getResponse = await client.GetAsync(
            $"/api/cloud-provider-credentials/{created.Credential.Id}"
        );
        getResponse.EnsureSuccessStatusCode();
        string getBody = await getResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(originalToken, getBody, StringComparison.Ordinal);
        Assert.DoesNotContain("apitoken", getBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credentials", getBody, StringComparison.OrdinalIgnoreCase);

        // Listing must show the credential set, and must not leak its secret material either.
        HttpResponseMessage listResponse = await client.GetAsync("/api/cloud-provider-credentials");
        listResponse.EnsureSuccessStatusCode();
        string listBody = await listResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(originalToken, listBody, StringComparison.Ordinal);

        // PUT fully replaces the credential set, including rotating its own secret material --
        // unlike a TSIG key's shared secret, this project deliberately allows this, since a
        // cloud provider credential is operator-supplied and the caller already knows the new
        // plaintext value they are submitting.
        const string rotatedToken = "cf-rotated-token-value";
        string renamedName = $"renamed-{Guid.NewGuid():N}";
        HttpResponseMessage replaceResponse = await client.PutAsJsonAsync(
            $"/api/cloud-provider-credentials/{created.Credential.Id}",
            new
            {
                name = renamedName,
                providerType = "Cloudflare",
                credentials = new Dictionary<string, string> { ["ApiToken"] = rotatedToken },
                isDisabled = true,
                comments = "replaced via PUT",
            }
        );
        Assert.Equal(HttpStatusCode.NoContent, replaceResponse.StatusCode);

        HttpResponseMessage afterReplaceResponse = await client.GetAsync(
            $"/api/cloud-provider-credentials/{created.Credential.Id}"
        );
        CloudProviderCredentialDetail replaced =
            await afterReplaceResponse.Content.ReadFromJsonAsync<CloudProviderCredentialDetail>();
        Assert.Equal(created.Credential.Id, replaced.Id);
        Assert.Equal(renamedName, replaced.Name);
        Assert.True(replaced.IsDisabled);
        Assert.Equal("replaced via PUT", replaced.Comments);

        // PUT's own response never echoes the rotated secret back either.
        string replaceBody = await replaceResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(rotatedToken, replaceBody, StringComparison.Ordinal);

        // PATCH via JSON Merge Patch, omitting 'credentials', only touches metadata -- the
        // stored secret material (already rotated above) is left untouched.
        HttpResponseMessage patchResponse = await client.SendAsync(
            PatchRequest(
                $"/api/cloud-provider-credentials/{created.Credential.Id}",
                new { isDisabled = false }
            )
        );
        Assert.Equal(HttpStatusCode.NoContent, patchResponse.StatusCode);

        HttpResponseMessage afterPatchResponse = await client.GetAsync(
            $"/api/cloud-provider-credentials/{created.Credential.Id}"
        );
        CloudProviderCredentialDetail patched =
            await afterPatchResponse.Content.ReadFromJsonAsync<CloudProviderCredentialDetail>();
        Assert.False(patched.IsDisabled);
        // The field the patch never mentioned must survive untouched.
        Assert.Equal(renamedName, patched.Name);
        Assert.Equal("replaced via PUT", patched.Comments);

        // Deleting works and is reflected immediately.
        HttpResponseMessage deleteResponse = await client.DeleteAsync(
            $"/api/cloud-provider-credentials/{created.Credential.Id}"
        );
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        HttpResponseMessage afterDeleteResponse = await client.GetAsync(
            $"/api/cloud-provider-credentials/{created.Credential.Id}"
        );
        Assert.Equal(HttpStatusCode.NotFound, afterDeleteResponse.StatusCode);
    }

    [Fact]
    public async Task CreateCloudProviderCredential_ForRoute53_RequiresBothAccessKeyFields()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        // Missing SecretAccessKey must be rejected.
        HttpResponseMessage missingSecretResponse = await client.PostAsJsonAsync(
            "/api/cloud-provider-credentials",
            new
            {
                name = $"route53-{Guid.NewGuid():N}",
                providerType = "Route53",
                credentials = new Dictionary<string, string> { ["AccessKeyId"] = "AKIAEXAMPLE" },
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, missingSecretResponse.StatusCode);

        // Supplying both required fields must succeed, and both are revealed exactly once.
        HttpResponseMessage okResponse = await client.PostAsJsonAsync(
            "/api/cloud-provider-credentials",
            new
            {
                name = $"route53-{Guid.NewGuid():N}",
                providerType = "Route53",
                credentials = new Dictionary<string, string>
                {
                    ["AccessKeyId"] = "AKIAEXAMPLE",
                    ["SecretAccessKey"] = "s3cr3t-access-key",
                },
            }
        );
        Assert.Equal(HttpStatusCode.Created, okResponse.StatusCode);
        CloudProviderCredentialCreateResponse created =
            await okResponse.Content.ReadFromJsonAsync<CloudProviderCredentialCreateResponse>();
        Assert.Equal("AKIAEXAMPLE", created.Secrets["AccessKeyId"]);
        Assert.Equal("s3cr3t-access-key", created.Secrets["SecretAccessKey"]);
    }

    [Fact]
    public async Task CreateCloudProviderCredential_ForCloudflareWithoutApiToken_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/cloud-provider-credentials",
            new
            {
                name = $"missing-token-{Guid.NewGuid():N}",
                providerType = "Cloudflare",
                credentials = new Dictionary<string, string>(),
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateCloudProviderCredential_WithAnEmptyName_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/cloud-provider-credentials",
            new
            {
                name = "",
                providerType = "Cloudflare",
                credentials = new Dictionary<string, string> { ["ApiToken"] = "irrelevant" },
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CloudProviderCredentialEndpoints_WithoutAToken_ReturnUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/cloud-provider-credentials");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
