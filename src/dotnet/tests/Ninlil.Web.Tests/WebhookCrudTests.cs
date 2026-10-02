using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the webhook subscription endpoints of <see cref="WebhooksController" />
///     over HTTP on the shared in-process host, signed in as the bootstrap administrator: the
///     create, list, get, replace, patch and delete lifecycle, the authentication credentials and
///     HMAC signing secret being returned once on creation and left out of every later response,
///     validation of event type names against <c>DnsServerEventTypeRegistry</c> and of the target
///     URL and authentication mode, and rejection without a token. It also covers
///     <c>GET /api/webhooks/{id}/delivery-attempts</c>, with attempt rows saved directly through
///     <see cref="IWebhookDeliveryAttemptRepository" />; the delivery engine that writes them is
///     covered in the <c>Server.Webhooks</c> tests. Some subscriptions created here are left
///     enabled, pointing at <c>https://example.invalid/hook</c>.
/// </summary>
[Collection(WebTestCollection.Name)]
public class WebhookCrudTests(NinlilDnsWebApplicationFactory factory)
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
    public async Task WebhookLifecycle_CreateListGetReplacePatchAndDelete_WorksEndToEnd()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string name = $"webhook-{Guid.NewGuid():N}";

        // Creating a Bearer-authenticated, HMAC-signed subscription returns both secrets in this
        // response only.
        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/webhooks",
            new
            {
                name,
                targetUrl = "https://example.invalid/hook",
                eventTypeNames = new[] { "DnsZoneChanged", "DnsCacheEvicted" },
                authMode = "Bearer",
                authCredentials = new Dictionary<string, string> { ["BearerToken"] = "s3cr3t" },
                enableHmacSigning = true,
                maxDeliveryAttempts = 3,
                comments = "created by test",
            }
        );
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        WebhookSubscriptionCreateResponse created =
            await createResponse.Content.ReadFromJsonAsync<WebhookSubscriptionCreateResponse>();
        Assert.Equal(name, created.Subscription.Name);
        Assert.Equal("https://example.invalid/hook", created.Subscription.TargetUrl);
        Assert.Equal(["DnsZoneChanged", "DnsCacheEvicted"], created.Subscription.EventTypeNames);
        Assert.Equal(WebhookAuthModeEnum.Bearer, created.Subscription.AuthMode);
        Assert.True(created.Subscription.HasHmacSigningSecret);
        Assert.Equal(3, created.Subscription.MaxDeliveryAttempts);
        Assert.False(created.Subscription.IsDisabled);
        Assert.Equal("s3cr3t", created.AuthCredentials["BearerToken"]);
        Assert.False(string.IsNullOrWhiteSpace(created.HmacSigningSecret));

        // Duplicate name must be rejected.
        HttpResponseMessage duplicateResponse = await client.PostAsJsonAsync(
            "/api/webhooks",
            new
            {
                name,
                targetUrl = "https://example.invalid/other",
                eventTypeNames = new[] { "DnsZoneChanged" },
            }
        );
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);

        // The list shows it, with pagination fields, and neither secret.
        HttpResponseMessage listResponse = await client.GetAsync("/api/webhooks");
        listResponse.EnsureSuccessStatusCode();
        string listBody = await listResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("s3cr3t", listBody);
        Assert.DoesNotContain(created.HmacSigningSecret, listBody);
        PagedResult<WebhookSubscriptionDetail> page = await listResponse.Content.ReadFromJsonAsync<
            PagedResult<WebhookSubscriptionDetail>
        >();
        Assert.Contains(page.Items, item => item.Id == created.Subscription.Id);

        // GET by identifier returns neither secret.
        HttpResponseMessage getResponse = await client.GetAsync(
            $"/api/webhooks/{created.Subscription.Id}"
        );
        getResponse.EnsureSuccessStatusCode();
        string getBody = await getResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("s3cr3t", getBody);
        Assert.DoesNotContain(created.HmacSigningSecret, getBody);
        WebhookSubscriptionDetail fetched =
            await getResponse.Content.ReadFromJsonAsync<WebhookSubscriptionDetail>();
        Assert.Equal(created.Subscription.Id, fetched.Id);
        Assert.True(fetched.HasHmacSigningSecret);

        // PUT replaces metadata and the authentication credentials. The operator supplies those,
        // so returning them is not an issue (see WebhookSubscriptionWriteRequest).
        HttpResponseMessage replaceResponse = await client.PutAsJsonAsync(
            $"/api/webhooks/{created.Subscription.Id}",
            new
            {
                name,
                targetUrl = "https://example.invalid/replaced",
                eventTypeNames = new[] { "DnssecZoneResigningCompleted" },
                authMode = "Basic",
                authCredentials = new Dictionary<string, string>
                {
                    ["Username"] = "operator",
                    ["Password"] = "hunter2",
                },
                maxDeliveryAttempts = 5,
                isDisabled = true,
            }
        );
        Assert.Equal(HttpStatusCode.NoContent, replaceResponse.StatusCode);

        HttpResponseMessage afterReplaceResponse = await client.GetAsync(
            $"/api/webhooks/{created.Subscription.Id}"
        );
        WebhookSubscriptionDetail replaced =
            await afterReplaceResponse.Content.ReadFromJsonAsync<WebhookSubscriptionDetail>();
        Assert.Equal(created.Subscription.Id, replaced.Id);
        Assert.Equal("https://example.invalid/replaced", replaced.TargetUrl);
        Assert.Equal(["DnssecZoneResigningCompleted"], replaced.EventTypeNames);
        Assert.Equal(WebhookAuthModeEnum.Basic, replaced.AuthMode);
        Assert.Equal(5, replaced.MaxDeliveryAttempts);
        Assert.True(replaced.IsDisabled);
        // The HMAC signing secret set at creation survives the PUT, which cannot change it (see
        // WebhookSubscriptionWriteRequest).
        Assert.True(replaced.HasHmacSigningSecret);

        // PATCH changes only the comments.
        HttpResponseMessage patchResponse = await client.SendAsync(
            PatchRequest($"/api/webhooks/{created.Subscription.Id}", new { comments = "patched" })
        );
        Assert.Equal(HttpStatusCode.NoContent, patchResponse.StatusCode);

        HttpResponseMessage afterPatchResponse = await client.GetAsync(
            $"/api/webhooks/{created.Subscription.Id}"
        );
        WebhookSubscriptionDetail patched =
            await afterPatchResponse.Content.ReadFromJsonAsync<WebhookSubscriptionDetail>();
        Assert.Equal("patched", patched.Comments);
        Assert.Equal("https://example.invalid/replaced", patched.TargetUrl);
        Assert.True(patched.IsDisabled);

        // Delete it.
        HttpResponseMessage deleteResponse = await client.DeleteAsync(
            $"/api/webhooks/{created.Subscription.Id}"
        );
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        HttpResponseMessage afterDeleteResponse = await client.GetAsync(
            $"/api/webhooks/{created.Subscription.Id}"
        );
        Assert.Equal(HttpStatusCode.NotFound, afterDeleteResponse.StatusCode);
    }

    [Fact]
    public async Task Create_WithNoAuthAndNoHmacSigning_NeverRevealsEitherSecretField()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/webhooks",
            new
            {
                name = $"plain-{Guid.NewGuid():N}",
                targetUrl = "https://example.invalid/hook",
                eventTypeNames = new[] { "DnsZoneChanged" },
            }
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        WebhookSubscriptionCreateResponse created =
            await response.Content.ReadFromJsonAsync<WebhookSubscriptionCreateResponse>();
        Assert.Equal(WebhookAuthModeEnum.None, created.Subscription.AuthMode);
        Assert.False(created.Subscription.HasHmacSigningSecret);
        Assert.Null(created.AuthCredentials);
        Assert.Null(created.HmacSigningSecret);
    }

    [Fact]
    public async Task Create_WithAnUnknownEventTypeName_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/webhooks",
            new
            {
                name = $"badevent-{Guid.NewGuid():N}",
                targetUrl = "https://example.invalid/hook",
                eventTypeNames = new[] { "NotARealEventType" },
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithANonHttpTargetUrl_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/webhooks",
            new
            {
                name = $"badurl-{Guid.NewGuid():N}",
                targetUrl = "not-a-url-at-all",
                eventTypeNames = new[] { "DnsZoneChanged" },
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithBearerAuthModeAndNoBearerToken_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/webhooks",
            new
            {
                name = $"noauth-{Guid.NewGuid():N}",
                targetUrl = "https://example.invalid/hook",
                eventTypeNames = new[] { "DnsZoneChanged" },
                authMode = "Bearer",
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task WebhooksEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/webhooks");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListDeliveryAttempts_ForARealSubscription_ReturnsItsPersistedHistory()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string name = $"delivery-history-{Guid.NewGuid():N}";

        // Create a subscription through the API, as an operator would; the attempt rows below
        // refer to its identifier.
        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/webhooks",
            new
            {
                name,
                targetUrl = "https://example.invalid/hook",
                eventTypeNames = new[] { "DnsZoneChanged" },
            }
        );
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        WebhookSubscriptionCreateResponse created =
            await createResponse.Content.ReadFromJsonAsync<WebhookSubscriptionCreateResponse>();

        // Save two delivery-attempt rows through the repository, in the shape
        // WebhookDeliveryService and WebhookDeliveryRetrySweepService write, and check what the
        // endpoint returns.
        Guid succeededAttemptId = Guid.NewGuid();
        Guid pendingAttemptId = Guid.NewGuid();
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IWebhookDeliveryAttemptRepository attemptRepository =
                scope.ServiceProvider.GetRequiredService<IWebhookDeliveryAttemptRepository>();
            IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await attemptRepository.AddAsync(
                new WebhookDeliveryAttemptEntity
                {
                    Id = succeededAttemptId,
                    CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                    SubscriptionId = created.Subscription.Id,
                    SubscriptionName = name,
                    EventTypeName = "DnsZoneChanged",
                    PayloadJson = """{"eventType":"DnsZoneChanged"}""",
                    Status = WebhookDeliveryAttemptStatusEnum.Succeeded,
                    AttemptCount = 1,
                    MaxAttempts = 5,
                    LastAttemptedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                    LastHttpStatusCode = 200,
                    CompletedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                }
            );
            await attemptRepository.AddAsync(
                new WebhookDeliveryAttemptEntity
                {
                    Id = pendingAttemptId,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    SubscriptionId = created.Subscription.Id,
                    SubscriptionName = name,
                    EventTypeName = "DnsZoneChanged",
                    PayloadJson = """{"eventType":"DnsZoneChanged"}""",
                    Status = WebhookDeliveryAttemptStatusEnum.Pending,
                    AttemptCount = 1,
                    MaxAttempts = 5,
                    LastAttemptedAtUtc = DateTimeOffset.UtcNow,
                    NextRetryDueUtc = DateTimeOffset.UtcNow.AddSeconds(30),
                    LastHttpStatusCode = 503,
                    LastErrorMessage = "Service Unavailable",
                }
            );
            await unitOfWork.SaveChangesAsync();
        }

        HttpResponseMessage historyResponse = await client.GetAsync(
            $"/api/webhooks/{created.Subscription.Id}/delivery-attempts"
        );
        historyResponse.EnsureSuccessStatusCode();
        PagedResult<WebhookDeliveryAttemptDetail> page =
            await historyResponse.Content.ReadFromJsonAsync<
                PagedResult<WebhookDeliveryAttemptDetail>
            >();

        Assert.Equal(2, page.TotalCount);
        // Newest first.
        Assert.Equal(pendingAttemptId, page.Items[0].Id);
        Assert.Equal(WebhookDeliveryAttemptStatusEnum.Pending, page.Items[0].Status);
        Assert.Equal(1, page.Items[0].AttemptCount);
        Assert.Equal(503, page.Items[0].LastHttpStatusCode);
        Assert.Equal("Service Unavailable", page.Items[0].LastErrorMessage);
        Assert.NotNull(page.Items[0].NextRetryDueUtc);

        Assert.Equal(succeededAttemptId, page.Items[1].Id);
        Assert.Equal(WebhookDeliveryAttemptStatusEnum.Succeeded, page.Items[1].Status);
        Assert.Equal(200, page.Items[1].LastHttpStatusCode);
        Assert.NotNull(page.Items[1].CompletedAtUtc);
    }

    [Fact]
    public async Task ListDeliveryAttempts_ForANonexistentSubscription_ReturnsNotFound()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync(
            $"/api/webhooks/{Guid.NewGuid()}/delivery-attempts"
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListDeliveryAttemptsEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(
            $"/api/webhooks/{Guid.NewGuid()}/delivery-attempts"
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
