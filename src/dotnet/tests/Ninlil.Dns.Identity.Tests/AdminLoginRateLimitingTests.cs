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
///     This class tests the request-time behavior of
///     <see cref="ServiceCollectionAdminLoginRateLimitingExtension" />: a 429 past the limit, reset
///     when the window rolls over, and no limiting when disabled. Each test builds a minimal
///     ASP.NET Core host on <see cref="TestServer" /> with one protected endpoint, instead of the
///     full <c>Ninlil.Web.Program</c>.
///     <para>
///         <b>Why not <c>NinlilDnsWebApplicationFactory</c>:</b> its in-memory transport leaves
///         <c>HttpContext.Connection.RemoteIpAddress</c> <see langword="null" />, so every request
///         would fall into the shared "unknown" partition that many other test classes also hit
///         through <c>POST /api/auth/login</c>. A dedicated host keeps the partition, limit and
///         window under the test's control.
///     </para>
/// </summary>
public class AdminLoginRateLimitingTests
{
    /// <summary>
    ///     This method starts a minimal <see cref="WebApplication" /> with only the rate limiter and
    ///     one endpoint protected by its policy, and returns an <see cref="HttpClient" /> for it over
    ///     the <see cref="TestServer" /> in-memory transport.
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
    ///     This test checks the main promise: attempt PermitLimit + 1 from one client within a
    ///     window is rejected with <c>429</c>, not logged or delayed.
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

            // Further attempts in the same window are rejected too.
            HttpResponseMessage stillRejected = await client.PostAsync("/login", content: null);
            Assert.Equal(HttpStatusCode.TooManyRequests, stillRejected.StatusCode);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    /// <summary>
    ///     This test checks that the lockout ends when the window rolls over, so a locked-out client
    ///     can sign in again without intervention; the lockout lasts
    ///     <see cref="Ninlil.Dns.Identity.Options.AdminLoginRateLimitOptions.Window" />.
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
    ///     This test checks that with <see cref="Ninlil.Dns.Identity.Options.AdminLoginRateLimitOptions.Enable" />
    ///     set to <see langword="false" />, many more attempts than <c>permitLimit</c> all succeed,
    ///     and the endpoint does not fail for referencing an unregistered policy (see
    ///     <see cref="ServiceCollectionAdminLoginRateLimitingExtension.AddNinlilDnsAdminLoginRateLimiting" />).
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
