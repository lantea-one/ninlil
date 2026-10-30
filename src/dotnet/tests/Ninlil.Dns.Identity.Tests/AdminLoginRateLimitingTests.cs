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
///     when the window rolls over, no limiting when disabled, and a separate count for the Forgot
///     Password policy. Each test builds a minimal ASP.NET Core host on <see cref="TestServer" />
///     with a sign-in and a Forgot Password endpoint, instead of the
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
    private const string TestAddressHeader = "X-Test-Remote-Address";

    /// <summary>
    ///     This method sends one POST to <paramref name="path" /> that the test host treats as
    ///     coming from <paramref name="remoteAddress" />.
    /// </summary>
    private static async Task<HttpStatusCode> PostFromAsync(
        HttpClient client,
        string path,
        string remoteAddress
    )
    {
        using HttpRequestMessage request = new(HttpMethod.Post, path);
        request.Headers.Add(TestAddressHeader, remoteAddress);
        using HttpResponseMessage response = await client.SendAsync(request);
        return response.StatusCode;
    }

    /// <summary>
    ///     This method starts a minimal <see cref="WebApplication" /> with only the rate limiter and
    ///     one endpoint for each of its policies, and returns an <see cref="HttpClient" /> for it over
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
        // TestServer leaves the remote address empty; this lets a test choose one per request.
        app.Use(
            (context, next) =>
            {
                if (
                    context.Request.Headers.TryGetValue(TestAddressHeader, out var value)
                    && IPAddress.TryParse(value, out IPAddress address)
                )
                    context.Connection.RemoteIpAddress = address;

                return next(context);
            }
        );
        app.UseRateLimiter();
        app.MapPost("/login", () => Results.Ok())
            .RequireRateLimiting(
                ServiceCollectionAdminLoginRateLimitingExtension.AuthenticationEndpointPolicyName
            );
        app.MapPost("/forgot-password", () => Results.Ok())
            .RequireRateLimiting(
                ServiceCollectionAdminLoginRateLimitingExtension.PasswordResetEndpointPolicyName
            );

        await app.StartAsync();

        return (app, app.GetTestClient());
    }

    /// <summary>
    ///     This test checks that the Forgot Password policy limits too, so an anonymous caller
    ///     cannot make the server send unlimited reset e-mails, and that it counts apart from
    ///     sign-in attempts.
    /// </summary>
    [Fact]
    public async Task ForgotPassword_PastThePermitLimit_IsRejectedWithoutUsingUpSignInAttempts()
    {
        (WebApplication app, HttpClient client) = await StartHostAsync(
            enable: true,
            permitLimit: 2,
            window: TimeSpan.FromMinutes(5)
        );

        try
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                HttpResponseMessage response = await client.PostAsync(
                    "/forgot-password",
                    content: null
                );
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            HttpResponseMessage rejected = await client.PostAsync(
                "/forgot-password",
                content: null
            );
            Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);

            HttpResponseMessage login = await client.PostAsync("/login", content: null);
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }
        finally
        {
            await app.StopAsync();
        }
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
    ///     This test checks that the <c>429</c> carries <c>Retry-After</c> (RFC 9110 §10.2.3) with
    ///     the whole seconds until the fixed window ends, never more than the window and at least
    ///     one second, so a client knows how long to wait.
    /// </summary>
    [Fact]
    public async Task Login_PastThePermitLimit_SendsRetryAfterWithinTheWindow()
    {
        (WebApplication app, HttpClient client) = await StartHostAsync(
            enable: true,
            permitLimit: 1,
            window: TimeSpan.FromMinutes(5)
        );

        try
        {
            Assert.Equal(
                HttpStatusCode.OK,
                (await client.PostAsync("/login", content: null)).StatusCode
            );

            HttpResponseMessage rejected = await client.PostAsync("/login", content: null);

            Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
            TimeSpan? retryAfter = rejected.Headers.RetryAfter?.Delta;
            Assert.NotNull(retryAfter);
            Assert.InRange(retryAfter.Value, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(5));
            Assert.Equal(TimeSpan.FromSeconds((int)retryAfter.Value.TotalSeconds), retryAfter);

            // The Forgot Password policy rejects the same way.
            Assert.Equal(
                HttpStatusCode.OK,
                (await client.PostAsync("/forgot-password", content: null)).StatusCode
            );
            HttpResponseMessage resetRejected = await client.PostAsync(
                "/forgot-password",
                content: null
            );
            Assert.Equal(HttpStatusCode.TooManyRequests, resetRejected.StatusCode);
            Assert.NotNull(resetRejected.Headers.RetryAfter?.Delta);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    /// <summary>
    ///     This test checks that <c>Retry-After</c> is the time left in the window, not the whole
    ///     window: three seconds into an eight-second window it is at most six seconds.
    /// </summary>
    [Fact]
    public async Task Login_PartWayThroughTheWindow_SendsTheTimeLeftNotTheWholeWindow()
    {
        (WebApplication app, HttpClient client) = await StartHostAsync(
            enable: true,
            permitLimit: 1,
            window: TimeSpan.FromSeconds(8)
        );

        try
        {
            Assert.Equal(
                HttpStatusCode.OK,
                (await client.PostAsync("/login", content: null)).StatusCode
            );
            await Task.Delay(TimeSpan.FromSeconds(3));

            HttpResponseMessage rejected = await client.PostAsync("/login", content: null);

            Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
            Assert.InRange(
                rejected.Headers.RetryAfter.Delta.Value,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(6)
            );
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

    /// <summary>
    ///     This test checks that two IPv6 addresses in one /64 share a partition, so an attacker
    ///     cannot get a fresh budget by rotating the low 64 bits. It covers both policies.
    /// </summary>
    [Theory]
    [InlineData("/login")]
    [InlineData("/forgot-password")]
    public async Task IPv6_AddressesInOneSlash64_ShareAPartition(string path)
    {
        (WebApplication app, HttpClient client) = await StartHostAsync(
            enable: true,
            permitLimit: 1,
            window: TimeSpan.FromMinutes(5)
        );

        try
        {
            Assert.Equal(HttpStatusCode.OK, await PostFromAsync(client, path, "2001:db8:1:2::1"));
            Assert.Equal(
                HttpStatusCode.TooManyRequests,
                await PostFromAsync(client, path, "2001:db8:1:2:aaaa:bbbb:cccc:dddd")
            );
        }
        finally
        {
            await app.StopAsync();
        }
    }

    /// <summary>
    ///     This test checks that IPv6 addresses in different /64 networks do not share a
    ///     partition, for both policies.
    /// </summary>
    [Theory]
    [InlineData("/login")]
    [InlineData("/forgot-password")]
    public async Task IPv6_AddressesInDifferentSlash64s_UseSeparatePartitions(string path)
    {
        (WebApplication app, HttpClient client) = await StartHostAsync(
            enable: true,
            permitLimit: 1,
            window: TimeSpan.FromMinutes(5)
        );

        try
        {
            Assert.Equal(HttpStatusCode.OK, await PostFromAsync(client, path, "2001:db8:1:2::1"));
            Assert.Equal(HttpStatusCode.OK, await PostFromAsync(client, path, "2001:db8:1:3::1"));
            Assert.Equal(
                HttpStatusCode.TooManyRequests,
                await PostFromAsync(client, path, "2001:db8:1:2::2")
            );
        }
        finally
        {
            await app.StopAsync();
        }
    }

    /// <summary>
    ///     This test checks that an IPv4 client and the same client seen as an IPv4-mapped IPv6
    ///     address (what a dual-stack socket reports) share one partition, and that a different
    ///     IPv4 address does not.
    /// </summary>
    [Fact]
    public async Task IPv4AndItsIPv4MappedForm_ShareAPartition()
    {
        (WebApplication app, HttpClient client) = await StartHostAsync(
            enable: true,
            permitLimit: 1,
            window: TimeSpan.FromMinutes(5)
        );

        try
        {
            Assert.Equal(HttpStatusCode.OK, await PostFromAsync(client, "/login", "192.0.2.9"));
            Assert.Equal(
                HttpStatusCode.TooManyRequests,
                await PostFromAsync(client, "/login", "::ffff:192.0.2.9")
            );
            Assert.Equal(HttpStatusCode.OK, await PostFromAsync(client, "/login", "192.0.2.10"));
        }
        finally
        {
            await app.StopAsync();
        }
    }
}
