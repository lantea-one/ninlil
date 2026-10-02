using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="IdentitySettingsController" /> (the ASP.NET Core Identity
///     password and lockout policy) over HTTP on the shared in-process host, signed in as the
///     bootstrap administrator: the compiled-in defaults, a patch checked by reading the host's
///     temporary settings file (<see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />)
///     and the returned note, and input validation. Values that the password generator cannot
///     meet, such as a required length above 24, are not tested.
/// </summary>
[Collection(WebTestCollection.Name)]
public class IdentitySettingsControllerTests(NinlilDnsWebApplicationFactory factory)
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
    public async Task Get_ReturnsTheCompiledInDefaultsWhenNothingHasBeenConfigured()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync("/api/identity-settings");
        response.EnsureSuccessStatusCode();

        IdentitySettingsDetail detail =
            await response.Content.ReadFromJsonAsync<IdentitySettingsDetail>();

        // The compiled-in defaults from ServiceCollectionIdentityExtension.
        Assert.Equal(12, detail.RequiredLength);
        Assert.True(detail.RequireDigit);
        Assert.True(detail.RequireLowercase);
        Assert.True(detail.RequireUppercase);
        Assert.True(detail.RequireNonAlphanumeric);
        Assert.Equal(TimeSpan.FromMinutes(15), detail.DefaultLockoutTimeSpan);
        Assert.Equal(5, detail.MaxFailedAccessAttempts);
        Assert.True(detail.AllowedForNewUsers);
    }

    [Fact]
    public async Task Patch_WithNewValues_WritesThemToTheConfigurationFile()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        try
        {
            HttpResponseMessage response = await client.SendAsync(
                PatchRequest(
                    "/api/identity-settings",
                    new
                    {
                        requiredLength = 16,
                        maxFailedAccessAttempts = 3,
                        defaultLockoutTimeSpan = "00:30:00",
                    }
                )
            );
            response.EnsureSuccessStatusCode();

            RuntimeSettingsWriteResult result =
                await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
            Assert.Equal(RuntimeSettingsWriteResult.HotReloadedNote, result.Note);

            // Only the file is checked; a GET would still show the old values (see
            // NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath). The hot reload is
            // covered by IdentityOptionsMonitorProxyHotReloadTests in Ninlil.Dns.Identity.Tests.
            JsonObject root = (JsonObject)
                JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
            JsonObject identity = (JsonObject)root["Identity"];
            Assert.Equal(16, identity["Password"]["RequiredLength"].GetValue<int>());
            Assert.Equal(3, identity["Lockout"]["MaxFailedAccessAttempts"].GetValue<int>());
            Assert.Equal(
                "00:30:00",
                identity["Lockout"]["DefaultLockoutTimeSpan"].GetValue<string>()
            );
        }
        finally
        {
            // Write the default policy back to the shared settings file; other tests sign in
            // with fixed passwords that rely on it.
            await client.SendAsync(
                PatchRequest(
                    "/api/identity-settings",
                    new
                    {
                        requiredLength = 12,
                        maxFailedAccessAttempts = 5,
                        defaultLockoutTimeSpan = "00:15:00",
                    }
                )
            );
        }
    }

    [Fact]
    public async Task Patch_WithARequiredLengthOfZero_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/identity-settings", new { requiredLength = 0 })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAnUnparseableLockoutTimeSpan_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/identity-settings",
                new { defaultLockoutTimeSpan = "not-a-timespan" }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAnEmptyBody_ReturnsOkWithoutWritingAnything()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/identity-settings", new { })
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Contains("nothing to change", result.Note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IdentitySettingsEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/identity-settings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
