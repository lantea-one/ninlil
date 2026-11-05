using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the rate limit on the two anonymous token endpoints,
///     <c>POST /api/auth/reset-password</c> (<see cref="Controllers.AuthController.ResetPassword" />)
///     and <c>POST /api/auth/confirm-email-change</c>
///     (<see cref="Controllers.AuthController.ConfirmEmailChange" />). Each test runs on a second host
///     from <see cref="NinlilDnsWebApplicationFactory.CreateSecondHost" /> with the admin sign-in
///     limiter switched on and a limit of two requests, because the shared host keeps the limiter
///     off so unrelated classes never meet a 429. The requests carry a random user id and token, so
///     each one answers 400 until the limit is reached.
/// </summary>
[Collection(WebTestCollection.Name)]
public class AnonymousTokenEndpointRateLimitTests(NinlilDnsWebApplicationFactory factory)
{
    private const int PermitLimit = 2;

    private WebApplicationFactory<Program> CreateLimitedHost() =>
        factory.CreateSecondHost(builder =>
        {
            builder.UseSetting("DnsServer:AdminLoginRateLimit:Enable", "true");
            builder.UseSetting("DnsServer:AdminLoginRateLimit:PermitLimit", PermitLimit.ToString());
            builder.UseSetting("DnsServer:AdminLoginRateLimit:Window", "00:05:00");
        });

    private static async Task AssertLimitedAsync(HttpClient client, string url, object body)
    {
        for (int i = 0; i < PermitLimit; i++)
        {
            HttpResponseMessage allowed = await client.PostAsJsonAsync(url, body);
            Assert.Equal(HttpStatusCode.BadRequest, allowed.StatusCode);
        }

        HttpResponseMessage limited = await client.PostAsJsonAsync(url, body);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task ResetPassword_PastTheLimit_Answers429WithRetryAfter()
    {
        using WebApplicationFactory<Program> host = CreateLimitedHost();

        await AssertLimitedAsync(
            host.CreateClient(),
            "/api/auth/reset-password",
            new ConfirmPasswordResetRequest(Guid.NewGuid(), "bad-token", "New-Password-1234!!")
        );
    }

    [Fact]
    public async Task ConfirmEmailChange_PastTheLimit_Answers429WithRetryAfter()
    {
        using WebApplicationFactory<Program> host = CreateLimitedHost();

        await AssertLimitedAsync(
            host.CreateClient(),
            "/api/auth/confirm-email-change",
            new ConfirmEmailChangeRequest(Guid.NewGuid(), "someone@example.com", "bad-token")
        );
    }
}
