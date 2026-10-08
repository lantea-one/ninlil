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
///     This class covers <see cref="DnsPipelineSettingsController" /> (worker pool sizing and UDP
///     amplification limits of <c>DnsQueryProcessingPipelineOptions</c>) over HTTP on the shared
///     in-process host, signed in as the bootstrap administrator. Patches are checked by reading
///     the host's temporary settings file
///     (<see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />) and by the
///     returned note, which has three forms because <c>workerCount</c>,
///     <c>maxConcurrentQueries</c> and <c>channelCapacity</c> need a worker restart while the three UDP fields hot-reload. Each
///     patch test writes the defaults back afterwards.
/// </summary>
[Collection(WebTestCollection.Name)]
public class DnsPipelineSettingsControllerTests(NinlilDnsWebApplicationFactory factory)
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

        HttpResponseMessage response = await client.GetAsync("/api/dns-pipeline-settings");
        response.EnsureSuccessStatusCode();

        DnsPipelineSettingsDetail detail =
            await response.Content.ReadFromJsonAsync<DnsPipelineSettingsDetail>();

        // The compiled-in defaults. WorkerCount defaults to Environment.ProcessorCount, which
        // varies by machine, so it is only checked to be positive.
        Assert.True(detail.WorkerCount > 0);
        Assert.Equal(1000, detail.MaxConcurrentQueries);
        Assert.Equal(4096, detail.ChannelCapacity);
        Assert.Equal(1232, detail.MaxUdpPayloadSizeOctets);
        Assert.Equal(20, detail.MaxUdpAmplificationFactor);
        Assert.Equal(512, detail.MinUdpAmplificationFloorSizeOctets);
    }

    [Fact]
    public async Task Patch_WithOnlyWorkerAndChannelFields_ReturnsTheWorkerRestartRequiredNote()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        try
        {
            HttpResponseMessage response = await client.SendAsync(
                PatchRequest(
                    "/api/dns-pipeline-settings",
                    new { workerCount = 4, channelCapacity = 8192 }
                )
            );
            response.EnsureSuccessStatusCode();

            RuntimeSettingsWriteResult result =
                await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
            Assert.Equal(RuntimeSettingsWriteResult.PipelineWorkerRestartRequiredNote, result.Note);

            JsonObject root = (JsonObject)
                JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
            JsonObject section = (JsonObject)root["DnsServer"]["Pipeline"];
            Assert.Equal(4, section["WorkerCount"].GetValue<int>());
            Assert.Equal(8192, section["ChannelCapacity"].GetValue<int>());
        }
        finally
        {
            await client.SendAsync(
                PatchRequest(
                    "/api/dns-pipeline-settings",
                    new { workerCount = Environment.ProcessorCount, channelCapacity = 4096 }
                )
            );
        }
    }

    [Fact]
    public async Task Patch_WithOnlyAUdpSafetyField_ReturnsTheHotReloadedNote()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        try
        {
            HttpResponseMessage response = await client.SendAsync(
                PatchRequest("/api/dns-pipeline-settings", new { maxUdpAmplificationFactor = 10.0 })
            );
            response.EnsureSuccessStatusCode();

            RuntimeSettingsWriteResult result =
                await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
            Assert.Equal(RuntimeSettingsWriteResult.HotReloadedNote, result.Note);

            JsonObject root = (JsonObject)
                JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
            JsonObject section = (JsonObject)root["DnsServer"]["Pipeline"];
            Assert.Equal(10.0, section["MaxUdpAmplificationFactor"].GetValue<double>());
        }
        finally
        {
            await client.SendAsync(
                PatchRequest("/api/dns-pipeline-settings", new { maxUdpAmplificationFactor = 20.0 })
            );
        }
    }

    [Fact]
    public async Task Patch_WithAWorkerFieldAndAUdpSafetyField_ReturnsTheMixedReloadNote()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        try
        {
            HttpResponseMessage response = await client.SendAsync(
                PatchRequest(
                    "/api/dns-pipeline-settings",
                    new { channelCapacity = 2048, minUdpAmplificationFloorSizeOctets = 600 }
                )
            );
            response.EnsureSuccessStatusCode();

            RuntimeSettingsWriteResult result =
                await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
            Assert.Equal(RuntimeSettingsWriteResult.PipelineMixedReloadNote, result.Note);
        }
        finally
        {
            await client.SendAsync(
                PatchRequest(
                    "/api/dns-pipeline-settings",
                    new { channelCapacity = 4096, minUdpAmplificationFloorSizeOctets = 512 }
                )
            );
        }
    }

    [Fact]
    public async Task Patch_WithMaxConcurrentQueries_PersistsItAndReturnsTheWorkerRestartRequiredNote()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        try
        {
            HttpResponseMessage response = await client.SendAsync(
                PatchRequest("/api/dns-pipeline-settings", new { maxConcurrentQueries = 50 })
            );
            response.EnsureSuccessStatusCode();

            RuntimeSettingsWriteResult result =
                await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
            Assert.Equal(RuntimeSettingsWriteResult.PipelineWorkerRestartRequiredNote, result.Note);

            JsonObject root = (JsonObject)
                JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
            JsonObject section = (JsonObject)root["DnsServer"]["Pipeline"];
            Assert.Equal(50, section["MaxConcurrentQueries"].GetValue<int>());
        }
        finally
        {
            await client.SendAsync(
                PatchRequest("/api/dns-pipeline-settings", new { maxConcurrentQueries = 1000 })
            );
        }
    }

    [Fact]
    public async Task Patch_WithAZeroMaxConcurrentQueries_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/dns-pipeline-settings", new { maxConcurrentQueries = 0 })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAZeroWorkerCount_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/dns-pipeline-settings", new { workerCount = 0 })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithANegativeAmplificationFactor_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/dns-pipeline-settings", new { maxUdpAmplificationFactor = -1 })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAnEmptyBody_ReturnsOkWithoutWritingAnything()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/dns-pipeline-settings", new { })
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Contains("nothing to change", result.Note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DnsPipelineSettingsEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/dns-pipeline-settings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
