using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ninlil.Dns.Identity.Options;
using Ninlil.Web.Models;
using Ninlil.Web.Services;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the per-account limit on wrong current passwords for
///     <c>POST /api/users/me/change-password</c> and <c>POST /api/users/me/email</c>. Each test
///     runs on a second host from <see cref="NinlilDnsWebApplicationFactory.CreateSecondHost" />
///     whose <see cref="CurrentPasswordAttemptLimiter" /> allows <see cref="MaxAttempts" /> wrong
///     passwords, with e-mail change confirmation off, and on users created for the test, so the
///     bootstrap administrator's budget is never used.
/// </summary>
[Collection(WebTestCollection.Name)]
public class CurrentPasswordRateLimitTests(NinlilDnsWebApplicationFactory factory)
{
    private const string Password = "Original-Password-1234!";
    private const string NewPassword = "Another-Password-5678!";
    private const int MaxAttempts = 3;

    /// <summary>
    ///     This record is one account on the second host with a client signed in as it.
    /// </summary>
    private sealed record Account(string UserName, HttpClient Client);

    private WebApplicationFactory<Program> CreateHost() =>
        factory.CreateSecondHost(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<CurrentPasswordAttemptLimiter>();
                services.AddSingleton(new CurrentPasswordAttemptLimiter(MaxAttempts));
                services.Configure<EmailChangeOptions>(options =>
                    options.RequireConfirmation = false
                );
            })
        );

    private static async Task<HttpClient> SignInAsync(
        WebApplicationFactory<Program> host,
        string userName,
        string password
    )
    {
        HttpClient client = host.CreateClient();
        HttpResponseMessage login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(userName, password)
        );
        login.EnsureSuccessStatusCode();
        LoginResponse body = await login.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            body.AccessToken
        );
        return client;
    }

    private static async Task<Account> CreateAccountAsync(WebApplicationFactory<Program> host)
    {
        HttpClient admin = await SignInAsync(
            host,
            NinlilDnsWebApplicationFactory.AdminUserName,
            NinlilDnsWebApplicationFactory.AdminPassword
        );
        string userName = $"lockout-{Guid.NewGuid():N}";
        HttpResponseMessage created = await admin.PostAsJsonAsync(
            "/api/users",
            new CreateUserRequest(userName, Password, $"{userName}@localhost.local", "Lockout Test")
        );
        created.EnsureSuccessStatusCode();
        return new Account(userName, await SignInAsync(host, userName, Password));
    }

    private static Task<HttpResponseMessage> ChangePasswordAsync(Account account, string current) =>
        account.Client.PostAsJsonAsync(
            "/api/users/me/change-password",
            new ChangePasswordRequest(current, NewPassword)
        );

    private static Task<HttpResponseMessage> ChangeEmailAsync(Account account, string current) =>
        account.Client.PostAsJsonAsync(
            "/api/users/me/email",
            new ChangeEmailRequest($"changed-{Guid.NewGuid():N}@localhost.local", current)
        );

    private static async Task WasteAttemptsAsync(
        Func<Account, string, Task<HttpResponseMessage>> action,
        Account account
    )
    {
        for (int i = 0; i < MaxAttempts; i++)
        {
            HttpResponseMessage wrong = await action(account, "Wrong-Password-1!");
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
            Assert.Contains("PasswordMismatch", await wrong.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task ChangeMyPassword_PastTheLimit_AnswersTooManyRequestsEvenForTheCorrectPassword()
    {
        using WebApplicationFactory<Program> host = CreateHost();
        Account account = await CreateAccountAsync(host);
        Account other = await CreateAccountAsync(host);

        await WasteAttemptsAsync(ChangePasswordAsync, account);

        HttpResponseMessage blocked = await ChangePasswordAsync(account, Password);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.True(blocked.Headers.RetryAfter is not null);

        // The budget is shared with the e-mail change and belongs to the account alone.
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            (await ChangeEmailAsync(account, Password)).StatusCode
        );
        Assert.Equal(HttpStatusCode.OK, (await ChangeEmailAsync(other, Password)).StatusCode);
    }

    [Fact]
    public async Task ChangeMyEmail_PastTheLimit_AnswersTooManyRequestsEvenForTheCorrectPassword()
    {
        using WebApplicationFactory<Program> host = CreateHost();
        Account account = await CreateAccountAsync(host);
        Account other = await CreateAccountAsync(host);

        await WasteAttemptsAsync(ChangeEmailAsync, account);

        HttpResponseMessage blocked = await ChangeEmailAsync(account, Password);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.True(blocked.Headers.RetryAfter is not null);

        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            (await ChangePasswordAsync(account, Password)).StatusCode
        );
        Assert.Equal(HttpStatusCode.OK, (await ChangeEmailAsync(other, Password)).StatusCode);
    }

    [Fact]
    public async Task ChangeMyEmail_WithinTheLimit_AcceptsTheCorrectPasswordAndClearsTheCount()
    {
        using WebApplicationFactory<Program> host = CreateHost();
        Account account = await CreateAccountAsync(host);

        for (int i = 1; i < MaxAttempts; i++)
            Assert.Equal(
                HttpStatusCode.BadRequest,
                (await ChangeEmailAsync(account, "Wrong-Password-1!")).StatusCode
            );
        Assert.Equal(HttpStatusCode.OK, (await ChangeEmailAsync(account, Password)).StatusCode);

        // The success cleared the count. The change ended the session, so the next attempts
        // use a new sign-in; a full budget of wrong passwords is again answered with 400.
        Account again = new(account.UserName, await SignInAsync(host, account.UserName, Password));
        await WasteAttemptsAsync(ChangeEmailAsync, again);
    }
}
