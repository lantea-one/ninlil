using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <c>GET /status</c> over HTTP on the shared in-process host. A caller
///     without a token sees only the overall status; a caller with <c>status:read</c> (here the
///     bootstrap administrator, whose wildcard grants it) also gets one entry per health check,
///     and all nine registered checks must be present. A dead-lettered webhook delivery saved
///     through the repository must turn the <c>webhookDelivery</c> check Degraded.
/// </summary>
[Collection(WebTestCollection.Name)]
public class StatusEndpointTests(NinlilDnsWebApplicationFactory factory)
{
    /// <summary>
    ///     This constant contains every health-check name <c>Program.cs</c> registers; each must
    ///     appear in an authenticated response.
    /// </summary>
    private static readonly string[] ExpectedCheckNames =
    [
        "storage",
        "tlsCertificate",
        "forwarders",
        "eventBroker",
        "dnssec",
        "transports",
        "diskSpace",
        "cluster",
        "webhookDelivery",
    ];

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

    [Fact]
    public async Task Status_WithoutAToken_ReturnsHighLevelStatusOnly()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/status");

        // A Degraded or Unhealthy check still gives a 200: the endpoint reports status and is
        // not an availability gate.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        StatusResponse status = await response.Content.ReadFromJsonAsync<StatusResponse>();
        Assert.False(string.IsNullOrWhiteSpace(status.Status));
        Assert.Null(status.Components);
    }

    [Fact]
    public async Task Status_WithAnAuthenticatedStatusReadToken_ReturnsFullComponentBreakdown()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync("/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        StatusResponse status = await response.Content.ReadFromJsonAsync<StatusResponse>();
        Assert.False(string.IsNullOrWhiteSpace(status.Status));
        Assert.NotNull(status.Components);

        foreach (string expectedName in ExpectedCheckNames)
            Assert.Contains(status.Components, component => component.Name == expectedName);

        // Every component must have a status and a description, not just a name.
        foreach (StatusComponent component in status.Components)
        {
            Assert.False(string.IsNullOrWhiteSpace(component.Status));
            Assert.False(string.IsNullOrWhiteSpace(component.Description));
        }
    }

    [Fact]
    public async Task Status_WithARecentlyDeadLetteredDelivery_ReportsWebhookDeliveryAsDegraded()
    {
        // Save a recently dead-lettered delivery attempt, the final state
        // WebhookDeliveryService and WebhookDeliveryRetrySweepService write once MaxAttempts is
        // used up, and check that WebhookDeliveryHealthCheck finds it through the repository.
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IWebhookDeliveryAttemptRepository attemptRepository =
                scope.ServiceProvider.GetRequiredService<IWebhookDeliveryAttemptRepository>();
            IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await attemptRepository.AddAsync(
                new WebhookDeliveryAttemptEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                    SubscriptionId = Guid.NewGuid(),
                    SubscriptionName = $"status-check-probe-{Guid.NewGuid():N}",
                    EventTypeName = "DnsZoneChanged",
                    PayloadJson = """{"eventType":"DnsZoneChanged"}""",
                    Status = WebhookDeliveryAttemptStatusEnum.DeadLettered,
                    AttemptCount = 5,
                    MaxAttempts = 5,
                    LastAttemptedAtUtc = DateTimeOffset.UtcNow,
                    LastHttpStatusCode = 500,
                    LastErrorMessage = "Internal Server Error",
                    CompletedAtUtc = DateTimeOffset.UtcNow,
                }
            );
            await unitOfWork.SaveChangesAsync();
        }

        HttpClient client = await CreateAuthenticatedClientAsync();
        HttpResponseMessage response = await client.GetAsync("/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        StatusResponse status = await response.Content.ReadFromJsonAsync<StatusResponse>();
        StatusComponent webhookDeliveryComponent = Assert.Single(
            status.Components,
            component => component.Name == "webhookDelivery"
        );
        Assert.Equal("Degraded", webhookDeliveryComponent.Status);
    }
}
