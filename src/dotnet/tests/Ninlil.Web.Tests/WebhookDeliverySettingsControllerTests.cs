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
///     This class covers <see cref="WebhookDeliverySettingsController" /> end to end, against a
///     real, in-process host and an isolated, throwaway JSON configuration file (see
///     <see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />) -- closing a
///     real gap: <c>WebhookDeliveryOptions</c> already drove real retry/backoff timing
///     and outbound timeout for every webhook delivery, but had zero admin-facing REST/UI
///     configuration surface. Also proves the three distinct response notes this controller's own
///     doc comment describes, since <c>RequestTimeout</c> hot-reloads on a materially different
///     schedule than <c>InitialRetryDelay</c>/<c>MaxRetryDelay</c>.
/// </summary>
[Collection(WebTestCollection.Name)]
public class WebhookDeliverySettingsControllerTests(NinlilDnsWebApplicationFactory factory)
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

        HttpResponseMessage response = await client.GetAsync("/api/webhook-delivery-settings");
        response.EnsureSuccessStatusCode();

        WebhookDeliverySettingsDetail detail =
            await response.Content.ReadFromJsonAsync<WebhookDeliverySettingsDetail>();

        // WebhookDeliveryOptions' own compiled-in defaults.
        Assert.Equal(TimeSpan.FromSeconds(1), detail.InitialRetryDelay);
        Assert.Equal(TimeSpan.FromSeconds(30), detail.MaxRetryDelay);
        Assert.Equal(TimeSpan.FromSeconds(10), detail.RequestTimeout);
    }

    [Fact]
    public async Task Patch_WithOnlyRetryFields_ReturnsHotReloadedNote()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        try
        {
            HttpResponseMessage response = await client.SendAsync(
                PatchRequest(
                    "/api/webhook-delivery-settings",
                    new { initialRetryDelay = "00:00:02", maxRetryDelay = "00:01:00" }
                )
            );
            response.EnsureSuccessStatusCode();

            RuntimeSettingsWriteResult result =
                await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
            Assert.Equal(RuntimeSettingsWriteResult.HotReloadedNote, result.Note);

            JsonObject root = (JsonObject)
                JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
            JsonObject section = (JsonObject)root["DnsServer"]["Webhooks"]["Delivery"];
            Assert.Equal("00:00:02", section["InitialRetryDelay"].GetValue<string>());
            Assert.Equal("00:01:00", section["MaxRetryDelay"].GetValue<string>());
        }
        finally
        {
            await client.SendAsync(
                PatchRequest(
                    "/api/webhook-delivery-settings",
                    new { initialRetryDelay = "00:00:01", maxRetryDelay = "00:00:30" }
                )
            );
        }
    }

    [Fact]
    public async Task Patch_WithOnlyRequestTimeout_ReturnsTheEventualReloadNote()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        try
        {
            HttpResponseMessage response = await client.SendAsync(
                PatchRequest("/api/webhook-delivery-settings", new { requestTimeout = "00:00:20" })
            );
            response.EnsureSuccessStatusCode();

            RuntimeSettingsWriteResult result =
                await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
            Assert.Equal(RuntimeSettingsWriteResult.WebhookRequestTimeoutEventualNote, result.Note);

            JsonObject root = (JsonObject)
                JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
            JsonObject section = (JsonObject)root["DnsServer"]["Webhooks"]["Delivery"];
            Assert.Equal("00:00:20", section["RequestTimeout"].GetValue<string>());
        }
        finally
        {
            await client.SendAsync(
                PatchRequest("/api/webhook-delivery-settings", new { requestTimeout = "00:00:10" })
            );
        }
    }

    [Fact]
    public async Task Patch_WithRequestTimeoutAndARetryField_ReturnsTheMixedReloadNote()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        try
        {
            HttpResponseMessage response = await client.SendAsync(
                PatchRequest(
                    "/api/webhook-delivery-settings",
                    new { requestTimeout = "00:00:15", maxRetryDelay = "00:00:45" }
                )
            );
            response.EnsureSuccessStatusCode();

            RuntimeSettingsWriteResult result =
                await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
            Assert.Equal(RuntimeSettingsWriteResult.WebhookMixedReloadNote, result.Note);
        }
        finally
        {
            await client.SendAsync(
                PatchRequest(
                    "/api/webhook-delivery-settings",
                    new { requestTimeout = "00:00:10", maxRetryDelay = "00:00:30" }
                )
            );
        }
    }

    [Fact]
    public async Task Patch_WithAnUnparseableDuration_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/webhook-delivery-settings",
                new { initialRetryDelay = "not-a-timespan" }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAnEmptyBody_ReturnsOkWithoutWritingAnything()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/webhook-delivery-settings", new { })
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Contains("nothing to change", result.Note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WebhookDeliverySettingsEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/webhook-delivery-settings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
