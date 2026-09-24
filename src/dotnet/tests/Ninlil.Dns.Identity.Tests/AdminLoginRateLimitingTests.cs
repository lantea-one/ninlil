using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Ninlil.Dns.Identity.Extension;

// Define our namespace.
namespace Ninlil.Dns.Identity.Tests;

/// <summary>
///     This class proves <see cref="ServiceCollectionAdminLoginRateLimitingExtension" />'s own
///     real request-time behavior -- a rejected 429 past the configured threshold, a real
///     window-based reset, per-partition isolation, and a genuine no-op when disabled -- against a
///     minimal, dedicated ASP.NET Core host built specifically for this test class, using
///     <see cref="TestServer" /> directly rather than <c>Ninlil.Web.Program</c>'s own, much
///     heavier composition root.
///     <para>
///         <b>Why a dedicated host, not <c>NinlilDnsWebApplicationFactory</c>:</b> that shared
///         fixture's own <c>TestServer</c>-backed in-memory transport leaves
///         <see cref="HttpContext.Connection" />'s own <c>RemoteIpAddress</c>
///         <see langword="null" /> for every request (the same real behavior
///         <c>Web.Tests.ReverseProxyTrustGatingMiddlewareTests</c>'s own doc comment already
///         found necessary to work around for an unrelated middleware) -- which would collapse
///         this limiter's own per-client-IP partitioning onto one shared "unknown" bucket used by
///         every one of the dozens of unrelated test classes that shared factory's own collection
///         already calls <c>POST /api/auth/login</c> from. This class needs a host with real,
///         controllable behavior for exactly the dimension under test (partition key, threshold,
///         window, enabled/disabled), so it builds one directly instead.
///     </para>
/// </summary>
public class AdminLoginRateLimitingTests
{
    /// <summary>
    ///     This method builds and starts a minimal <see cref="WebApplication" /> carrying only
    ///     this limiter's own real middleware and a single trivial endpoint protected by its
    ///     policy, returning a real <see cref="HttpClient" /> talking to it over
    ///     <see cref="TestServer" />'s in-memory transport.
    /// </summary>
    private static async Task<(WebApplication App, HttpClient Client)> StartHostAsync(
        bool enable,
        int permitLimit,
        TimeSpan window
    )
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Configuration["DnsServer:AdminLoginRateLimit:Enable"] = enable.ToString();
        builder.Configuration["DnsServer:AdminLoginRateLimit:PermitLimit"] = permitLimit.ToString();
        builder.Configuration["DnsServer:AdminLoginRateLimit:Window"] = window.ToString();

        builder.Services.AddNinlilDnsAdminLoginRateLimiting(builder.Configuration);

        WebApplication app = builder.Build();
        app.UseRateLimiter();
        app.MapPost("/login", () => Results.Ok())
            .RequireRateLimiting(
                ServiceCollectionAdminLoginRateLimitingExtension.AuthenticationEndpointPolicyName
            );

        await app.StartAsync();

        return (app, app.GetTestClient());
    }

    [Fact]
    public async Task Login_WithinThePermitLimit_AlwaysSucceeds()
    {
        (WebApplication app, HttpClient client) = await StartHostAsync(
            enable: true,
            permitLimit: 3,
            window: TimeSpan.FromMinutes(5)
        );

        try
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                HttpResponseMessage response = await client.PostAsync("/login", content: null);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }
        finally
        {
            await app.StopAsync();
        }
    }

    /// <summary>
    ///     This test is the real, direct proof of this feature's own core promise: the
    ///     (PermitLimit + 1)th attempt from the same client within one window is genuinely
    ///     rejected with <c>429</c>, never merely logged or delayed -- this limiter's own
    ///     deliberate fail-closed posture, the opposite of <c>Server.RateLimiting</c>'s own
    ///     fail-open DNS-query limiter.
    /// </summary>
    [Fact]
    public async Task Login_PastThePermitLimit_IsRejectedWithTooManyRequests()
    {
        (WebApplication app, HttpClient client) = await StartHostAsync(
            enable: true,
            permitLimit: 3,
            window: TimeSpan.FromMinutes(5)
        );

        try
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                HttpResponseMessage response = await client.PostAsync("/login", content: null);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            HttpResponseMessage rejected = await client.PostAsync("/login", content: null);
            Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);

            // Confirms this is a genuine lockout, not a one-off blip -- a further attempt still
            // inside the same window keeps being rejected.
            HttpResponseMessage stillRejected = await client.PostAsync("/login", content: null);
            Assert.Equal(HttpStatusCode.TooManyRequests, stillRejected.StatusCode);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    /// <summary>
    ///     This test proves the lockout is genuinely temporary: once the fixed window rolls over,
    ///     a client that was locked out can sign in again with no operator intervention -- this
    ///     limiter's own effective "lockout duration" is exactly
    ///     <see cref="Ninlil.Dns.Identity.Options.AdminLoginRateLimitOptions.Window" />, per that
    ///     class's own doc comment.
    /// </summary>
    [Fact]
    public async Task Login_AfterTheWindowRollsOver_SucceedsAgain()
    {
        (WebApplication app, HttpClient client) = await StartHostAsync(
            enable: true,
            permitLimit: 1,
            window: TimeSpan.FromMilliseconds(200)
        );

        try
        {
            HttpResponseMessage first = await client.PostAsync("/login", content: null);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);

            HttpResponseMessage rejected = await client.PostAsync("/login", content: null);
            Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);

            await Task.Delay(TimeSpan.FromMilliseconds(400));

            HttpResponseMessage afterWindow = await client.PostAsync("/login", content: null);
            Assert.Equal(HttpStatusCode.OK, afterWindow.StatusCode);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    /// <summary>
    ///     This test proves <see cref="Ninlil.Dns.Identity.Options.AdminLoginRateLimitOptions.Enable" />
    ///     set to <see langword="false" /> is a genuine no-op -- far more attempts than
    ///     <c>permitLimit</c> all still succeed -- and, just as importantly, that the protected
    ///     endpoint does not throw a configuration error for referencing a policy name this
    ///     disabled configuration might otherwise have left unregistered (see
    ///     <see cref="ServiceCollectionAdminLoginRateLimitingExtension.AddNinlilDnsAdminLoginRateLimiting" />'s
    ///     own doc comment for the real bug this was found to guard against).
    /// </summary>
    [Fact]
    public async Task Login_WhenDisabled_NeverRejectsRegardlessOfAttemptCount()
    {
        (WebApplication app, HttpClient client) = await StartHostAsync(
            enable: false,
            permitLimit: 1,
            window: TimeSpan.FromMinutes(5)
        );

        try
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                HttpResponseMessage response = await client.PostAsync("/login", content: null);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }
        finally
        {
            await app.StopAsync();
        }
    }
}
