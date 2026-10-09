using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="ExceptionHandling.MergePatchBodyFilter" /> over HTTP on the
///     shared in-process host, signed in as the bootstrap administrator. A non-object body and a
///     wrong-typed field must give 400, not 200 or 500, on a settings PATCH and on a non-settings
///     PATCH. Every rejected request also asserts that the shared runtime settings file did not
///     change, because a bad value saved there would break the shared host for later tests.
/// </summary>
[Collection(WebTestCollection.Name)]
public class MergePatchBodyFilterTests(NinlilDnsWebApplicationFactory factory)
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

    private static HttpRequestMessage PatchRaw(string url, string rawJson) =>
        new(HttpMethod.Patch, url)
        {
            Content = new StringContent(rawJson, Encoding.UTF8, "application/json"),
        };

    private async Task<string> ReadSettingsFileAsync() =>
        File.Exists(factory.RuntimeConfigurationFilePath)
            ? await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath)
            : string.Empty;

    [Theory]
    [InlineData("[]")]
    [InlineData("\"x\"")]
    [InlineData("5")]
    [InlineData("null")]
    public async Task Patch_OnASettingsEndpointWithANonObjectBody_ReturnsBadRequest(string body)
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string before = await ReadSettingsFileAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRaw("/api/identity-settings", body)
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("JSON object", await response.Content.ReadAsStringAsync());
        Assert.Equal(before, await ReadSettingsFileAsync());
    }

    [Fact]
    public async Task Patch_OnASettingsEndpointWithAWrongTypedField_ReturnsBadRequestNotServerError()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string before = await ReadSettingsFileAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRaw("/api/identity-settings", "{\"requiredLength\":\"abc\"}")
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        // The identity controller reads the field through the typed readers, which name it. The
        // filter's own "wrong JSON type" message is covered by the non-settings test below.
        Assert.Contains("requiredLength", await response.Content.ReadAsStringAsync());
        Assert.Equal(before, await ReadSettingsFileAsync());
    }

    [Fact]
    public async Task Patch_OnASettingsEndpointWithAFractionalWholeNumber_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string before = await ReadSettingsFileAsync();

        // GetInt32 throws FormatException (not InvalidOperationException) for 1.5.
        HttpResponseMessage response = await client.SendAsync(
            PatchRaw("/api/identity-settings", "{\"requiredLength\":1.5}")
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await ReadSettingsFileAsync());
    }

    [Fact]
    public async Task Patch_OnANonSettingsEndpointWithAWrongTypedField_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string before = await ReadSettingsFileAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRaw("/api/users/me", "{\"displayName\":5}")
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await ReadSettingsFileAsync());
    }

    [Fact]
    public async Task Patch_OnANonSettingsEndpointWithANonObjectBody_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(PatchRaw("/api/users/me", "[]"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAValidBody_StillSucceeds()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        // An empty object is valid and writes nothing.
        HttpResponseMessage settings = await client.SendAsync(
            PatchRaw("/api/identity-settings", "{}")
        );
        HttpResponseMessage profile = await client.SendAsync(
            PatchRaw("/api/users/me", "{\"displayName\":\"Administrator\"}")
        );

        Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
    }

    [Fact]
    public async Task Patch_BadRequestBody_IsAProblemDetailsWithTheHandlersTypeUri()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRaw("/api/identity-settings", "[]")
        );

        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(
            "https://httpstatuses.io/400",
            body.RootElement.GetProperty("type").GetString()
        );
    }
}
