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
///     This class covers the <c>GET /status</c> health-check endpoint end to end,
///     against a real, in-process host and a real SQLite database (see
///     <see cref="NinlilDnsWebApplicationFactory" />) -- confirming the additive
///     unauthenticated/authenticated response shape this endpoint's design calls for: an unauthenticated
///     caller only ever sees the high-level aggregate status, while an authenticated caller
///     holding <c>status:read</c> (the bootstrap administrator's own wildcard-permission role
///     already satisfies this, like every other permission) additionally sees the full
///     per-component breakdown, naming every one of the 9 registered checks.
/// </summary>
[Collection(WebTestCollection.Name)]
public class StatusEndpointTests(NinlilDnsWebApplicationFactory factory)
{
    /// <summary>
    ///     This constant contains every health-check name <c>Program.cs</c> registers -- this
    ///     test asserts every one of them is actually present in an authenticated response,
    ///     confirming the full 9-item checklist, not merely "some entries exist."
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

        // A health check reporting Degraded/Unhealthy is still a successful HTTP response, not a
        // 4xx/5xx -- this endpoint serves operational status, not an availability gate on itself.
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

        // Every component must itself carry a real status string and description -- not just be
        // present by name.
        foreach (StatusComponent component in status.Components)
        {
            Assert.False(string.IsNullOrWhiteSpace(component.Status));
            Assert.False(string.IsNullOrWhiteSpace(component.Description));
        }
    }

    [Fact]
    public async Task Status_WithARecentlyDeadLetteredDelivery_ReportsWebhookDeliveryAsDegraded()
    {
        // Seed a real, persisted, recently dead-lettered delivery-attempt row -- exactly the
        // terminal state WebhookDeliveryService/WebhookDeliveryRetrySweepService themselves
        // write once a delivery's own MaxAttempts budget is fully exhausted -- and confirm
        // WebhookDeliveryHealthCheck picks it up as a real, pushed-down query against the same
        // repository, not a mocked/faked signal.
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
