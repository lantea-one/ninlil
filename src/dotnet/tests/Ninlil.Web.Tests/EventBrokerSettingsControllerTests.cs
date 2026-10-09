using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="EventBrokerSettingsController" /> over HTTP on the shared
///     in-process host, signed in as the bootstrap administrator: the in-memory transport the test
///     host runs with, a RabbitMQ patch checked by reading the host's temporary settings file
///     (<see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />) with no
///     password written when none was sent, and a rejected patch that writes nothing. The
///     returned note is compared with <see cref="RuntimeSettingsWriteResult.RestartRequiredNote" />.
/// </summary>
[Collection(WebTestCollection.Name)]
public class EventBrokerSettingsControllerTests(NinlilDnsWebApplicationFactory factory)
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
    public async Task Get_WithNoBrokerConfigured_ReturnsInMemoryWithNeitherBrokerSection()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync("/api/event-broker-settings");
        response.EnsureSuccessStatusCode();

        EventBrokerSettingsDetail detail =
            await response.Content.ReadFromJsonAsync<EventBrokerSettingsDetail>();

        Assert.Equal(EventTransportEnum.InMemory, detail.Transport);
        Assert.Null(detail.IsConnected);
        Assert.Null(detail.RabbitMq);
        Assert.Null(detail.ActiveMq);
    }

    [Fact]
    public async Task Patch_WithRabbitMqSettings_WritesThemToTheConfigurationFileWithoutTheNeverSuppliedPasswordAndOmitsUnsuppliedFields()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/event-broker-settings",
                new { rabbitMq = new { hostName = "broker.example.internal", port = 5673 } }
            )
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Equal(RuntimeSettingsWriteResult.RestartRequiredNote, result.Note);

        JsonObject root = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
        JsonObject rabbitMqSection = (JsonObject)root["DnsServer"]["Events"]["RabbitMq"];
        Assert.Equal("broker.example.internal", rabbitMqSection["HostName"].GetValue<string>());
        Assert.Equal(5673, rabbitMqSection["Port"].GetValue<int>());
        Assert.False(rabbitMqSection.ContainsKey("Password"));
    }

    [Fact]
    public async Task Patch_WithAnInvalidTransportName_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/event-broker-settings", new { transport = "NotARealTransport" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAnInvalidReconnectDelay_ReturnsBadRequestAndWritesNothing()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/event-broker-settings",
                new
                {
                    rabbitMq = new
                    {
                        hostName = "should-not-be-written.example.internal",
                        initialReconnectDelay = "not-a-timespan",
                    },
                }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        if (File.Exists(factory.RuntimeConfigurationFilePath))
        {
            string content = await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath);
            Assert.DoesNotContain("should-not-be-written.example.internal", content);
        }
    }

    [Fact]
    public async Task EventBrokerSettingsEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/event-broker-settings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
