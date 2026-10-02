using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Token;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers cookie delivery of the refresh token on the shared in-process host,
///     signed in as the bootstrap administrator: the cookie's security attributes, "Remember me"
///     choosing a persistent or a browser-session session (both in the cookie and in the
///     server-side lifetime, read from storage), rotation through the cookie keeping that choice,
///     sign-out clearing it, and body delivery staying as before. The <c>Set-Cookie</c> header is
///     read raw, from a client that does not handle cookies itself, so each attribute is checked
///     as a browser would receive it.
/// </summary>
[Collection(WebTestCollection.Name)]
public class RefreshTokenCookieTests(NinlilDnsWebApplicationFactory factory)
{
    /// <summary>
    ///     This method creates a client that leaves cookies alone, so each test decides which
    ///     cookie a request carries.
    /// </summary>
    private HttpClient CreateRawClient() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    /// <summary>
    ///     This method signs the bootstrap administrator in with cookie delivery.
    /// </summary>
    private static Task<HttpResponseMessage> CookieLoginAsync(HttpClient client, bool rememberMe) =>
        client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(
                NinlilDnsWebApplicationFactory.AdminUserName,
                NinlilDnsWebApplicationFactory.AdminPassword,
                rememberMe,
                RefreshTokenDeliveryEnum.Cookie
            )
        );

    /// <summary>
    ///     This method returns the response's refresh-cookie <c>Set-Cookie</c> header, or
    ///     <see langword="null" /> when it sets none.
    /// </summary>
    private static string RefreshSetCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string> values)
            ? values.FirstOrDefault(value =>
                value.StartsWith(RefreshTokenCookie.CookieName + "=", StringComparison.Ordinal)
            )
            : null;

    /// <summary>
    ///     This method extracts the raw cookie value from a <c>Set-Cookie</c> header.
    /// </summary>
    private static string CookieValue(string setCookie) =>
        setCookie.Split(';')[0][(RefreshTokenCookie.CookieName.Length + 1)..];

    /// <summary>
    ///     This method sends a cookie-mode request (an empty JSON body) carrying one refresh cookie.
    /// </summary>
    private static Task<HttpResponseMessage> PostWithCookieAsync(
        HttpClient client,
        string path,
        string cookieValue
    )
    {
        HttpRequestMessage request = new(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(new { }),
        };
        if (cookieValue is not null)
            request.Headers.Add("Cookie", $"{RefreshTokenCookie.CookieName}={cookieValue}");
        return client.SendAsync(request);
    }

    /// <summary>
    ///     This method loads the session a raw refresh token belongs to, straight from storage.
    /// </summary>
    private async Task<SessionEntity> FindSessionAsync(string rawRefreshToken)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<ISessionRepository>()
            .FindByRefreshTokenHashAsync(TokenHasher.Hash(rawRefreshToken));
    }

    [Fact]
    public async Task CookieLogin_WithoutRememberMe_SetsAHardenedBrowserSessionCookieAndNoBodyToken()
    {
        HttpClient client = CreateRawClient();

        HttpResponseMessage response = await CookieLoginAsync(client, rememberMe: false);
        response.EnsureSuccessStatusCode();
        LoginResponse login = await response.Content.ReadFromJsonAsync<LoginResponse>();

        // The access token still comes back; the refresh token never appears in the body.
        Assert.False(string.IsNullOrEmpty(login.AccessToken));
        Assert.Null(login.RefreshToken);

        string setCookie = RefreshSetCookie(response);
        Assert.NotNull(setCookie);
        string attributes = setCookie.ToLowerInvariant();
        Assert.Contains("httponly", attributes);
        Assert.Contains("samesite=strict", attributes);
        Assert.Contains("path=/api/auth", attributes);

        // No expiry: the browser drops it when it closes. The test host is plain HTTP, so the
        // cookie is not marked Secure (it is whenever the request arrived over HTTPS).
        Assert.DoesNotContain("expires=", attributes);
        Assert.DoesNotContain("secure", attributes);

        SessionEntity session = await FindSessionAsync(CookieValue(setCookie));
        Assert.False(session.IsPersistent);
        Assert.InRange(
            session.ExpiresAtUtc - DateTimeOffset.UtcNow,
            TimeSpan.FromHours(11),
            TimeSpan.FromHours(12)
        );
    }

    [Fact]
    public async Task CookieLogin_WithRememberMe_SetsAPersistentCookieForTheLongLifetime()
    {
        HttpClient client = CreateRawClient();

        HttpResponseMessage response = await CookieLoginAsync(client, rememberMe: true);
        response.EnsureSuccessStatusCode();

        string setCookie = RefreshSetCookie(response);
        Assert.Contains("expires=", setCookie.ToLowerInvariant());

        SessionEntity session = await FindSessionAsync(CookieValue(setCookie));
        Assert.True(session.IsPersistent);
        Assert.InRange(
            session.ExpiresAtUtc - DateTimeOffset.UtcNow,
            TimeSpan.FromDays(29),
            TimeSpan.FromDays(30)
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CookieRefresh_RotatesTheCookieKeepsTheSessionKindAndRejectsTheOldOne(
        bool rememberMe
    )
    {
        HttpClient client = CreateRawClient();
        HttpResponseMessage loginResponse = await CookieLoginAsync(client, rememberMe);
        string firstCookie = CookieValue(RefreshSetCookie(loginResponse));

        // A page reload: no token in the body, only the cookie.
        HttpResponseMessage refreshResponse = await PostWithCookieAsync(
            client,
            "/api/auth/refresh",
            firstCookie
        );
        refreshResponse.EnsureSuccessStatusCode();
        AuthenticationResult refreshed =
            await refreshResponse.Content.ReadFromJsonAsync<AuthenticationResult>();

        Assert.False(string.IsNullOrEmpty(refreshed.AccessToken));
        Assert.Null(refreshed.RefreshToken);
        string secondSetCookie = RefreshSetCookie(refreshResponse);
        Assert.NotNull(secondSetCookie);
        string secondCookie = CookieValue(secondSetCookie);
        Assert.NotEqual(firstCookie, secondCookie);
        Assert.Equal(rememberMe, secondSetCookie.ToLowerInvariant().Contains("expires="));
        Assert.Equal(rememberMe, (await FindSessionAsync(secondCookie)).IsPersistent);

        // The rotated-out cookie is dead -- but its failure must not clear the cookie: in a
        // browser that response would erase the fresh cookie another tab just received.
        HttpResponseMessage replay = await PostWithCookieAsync(
            client,
            "/api/auth/refresh",
            firstCookie
        );
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Null(RefreshSetCookie(replay));

        // The rotated cookie keeps working afterwards.
        Assert.Equal(
            HttpStatusCode.OK,
            (await PostWithCookieAsync(client, "/api/auth/refresh", secondCookie)).StatusCode
        );
    }

    [Fact]
    public async Task CookieLogout_RevokesTheSessionAndClearsTheCookie()
    {
        HttpClient client = CreateRawClient();
        string cookie = CookieValue(RefreshSetCookie(await CookieLoginAsync(client, true)));

        HttpResponseMessage logout = await PostWithCookieAsync(client, "/api/auth/logout", cookie);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Contains("expires=thu, 01 jan 1970", RefreshSetCookie(logout).ToLowerInvariant());
        Assert.NotNull((await FindSessionAsync(cookie)).RevokedAtUtc);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await PostWithCookieAsync(client, "/api/auth/refresh", cookie)).StatusCode
        );
    }

    [Fact]
    public async Task CookieRefresh_WithNoCookieAtAll_IsUnauthorized()
    {
        HttpClient client = CreateRawClient();

        HttpResponseMessage response = await PostWithCookieAsync(client, "/api/auth/refresh", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task BodyLogin_IsUnchanged_NoCookieAndALongLivedSession()
    {
        HttpClient client = CreateRawClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(
                NinlilDnsWebApplicationFactory.AdminUserName,
                NinlilDnsWebApplicationFactory.AdminPassword
            )
        );
        response.EnsureSuccessStatusCode();
        LoginResponse login = await response.Content.ReadFromJsonAsync<LoginResponse>();

        Assert.False(string.IsNullOrEmpty(login.RefreshToken));
        Assert.Null(RefreshSetCookie(response));
        Assert.True((await FindSessionAsync(login.RefreshToken)).IsPersistent);

        // Rotating a body-delivered token stays in the body and sets no cookie.
        HttpResponseMessage refresh = await client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshRequest(login.RefreshToken)
        );
        refresh.EnsureSuccessStatusCode();
        Assert.False(
            string.IsNullOrEmpty(
                (await refresh.Content.ReadFromJsonAsync<AuthenticationResult>()).RefreshToken
            )
        );
        Assert.Null(RefreshSetCookie(refresh));
    }
}
