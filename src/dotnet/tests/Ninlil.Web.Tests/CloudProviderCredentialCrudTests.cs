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
///     This class covers the cloud DNS provider credential endpoints over HTTP on the shared
///     in-process host, signed in as the bootstrap administrator: creating Cloudflare and Route 53
///     credential sets (DigitalOcean is not tested), the secrets being returned once on creation
///     and left out of every later response, replacing the secrets with <c>PUT</c> (allowed here,
///     unlike for TSIG keys; see <see cref="Models.CloudProviderCredentialWriteRequest" />),
///     editing metadata with <c>PATCH</c>, and deleting. The API never returns stored secrets, so
///     the tests cannot check that a rotation or a patch stored the right ones.
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

        // Creating a Cloudflare credential set returns its secrets in this response only.
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

        // GET never returns the secrets.
        HttpResponseMessage getResponse = await client.GetAsync(
            $"/api/cloud-provider-credentials/{created.Credential.Id}"
        );
        getResponse.EnsureSuccessStatusCode();
        string getBody = await getResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(originalToken, getBody, StringComparison.Ordinal);
        Assert.DoesNotContain("apitoken", getBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credentials", getBody, StringComparison.OrdinalIgnoreCase);

        // The list must not include the secrets either.
        HttpResponseMessage listResponse = await client.GetAsync("/api/cloud-provider-credentials");
        listResponse.EnsureSuccessStatusCode();
        string listBody = await listResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(originalToken, listBody, StringComparison.Ordinal);

        // PUT replaces the whole credential set, secrets included. Unlike a TSIG key's secret,
        // this is allowed, because the operator supplies these credentials and already knows the
        // new value.
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

        // The PUT response does not echo the new secret.
        string replaceBody = await replaceResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(rotatedToken, replaceBody, StringComparison.Ordinal);

        // A JSON Merge Patch without 'credentials' changes only metadata. (The stored secrets are
        // not readable, so their survival is not checked.)
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

        // Supplying both required fields succeeds, and both are returned on creation.
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
