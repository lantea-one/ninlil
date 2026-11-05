using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Identity.Options;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers how a wrong current password counts toward account lockout on the two
///     self-service actions that ask for it: <c>POST /api/users/me/change-password</c> and
///     <c>POST /api/users/me/email</c>. Each test runs on a second host from
///     <see cref="NinlilDnsWebApplicationFactory.CreateSecondHost" /> with a lockout limit of
///     <see cref="MaxAttempts" /> and e-mail change confirmation off, and on a user created for
///     the test, so the bootstrap administrator is never locked out. The tests read and clear the
///     lockout through the host's <c>UserManager</c>.
/// </summary>
[Collection(WebTestCollection.Name)]
public class CurrentPasswordLockoutTests(NinlilDnsWebApplicationFactory factory)
{
    private const string Password = "Original-Password-1234!";
    private const string NewPassword = "Another-Password-5678!";
    private const int MaxAttempts = 3;

    /// <summary>
    ///     This record is one account on the second host with a client signed in as it.
    /// </summary>
    private sealed record Account(Guid Id, string UserName, HttpClient Client);

    private WebApplicationFactory<Program> CreateHost() =>
        factory.CreateSecondHost(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.Configure<IdentityOptions>(options =>
                    options.Lockout.MaxFailedAccessAttempts = MaxAttempts
                );
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
        Guid id = (await created.Content.ReadFromJsonAsync<UserSummary>()).Id;
        return new Account(id, userName, await SignInAsync(host, userName, Password));
    }

    private static async Task<UserEntity> ReadUserAsync(
        WebApplicationFactory<Program> host,
        Guid id
    )
    {
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<UserManager<UserEntity>>()
            .FindByIdAsync(id.ToString());
    }

    private static async Task EndLockoutAsync(WebApplicationFactory<Program> host, Guid id)
    {
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        UserManager<UserEntity> userManager = scope.ServiceProvider.GetRequiredService<
            UserManager<UserEntity>
        >();
        UserEntity user = await userManager.FindByIdAsync(id.ToString());
        Assert.True((await userManager.SetLockoutEndDateAsync(user, null)).Succeeded);
        Assert.True((await userManager.ResetAccessFailedCountAsync(user)).Succeeded);
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

    private static async Task<HttpStatusCode> LoginStatusAsync(
        WebApplicationFactory<Program> host,
        Account account,
        string password
    ) =>
        (
            await host.CreateClient()
                .PostAsJsonAsync("/api/auth/login", new LoginRequest(account.UserName, password))
        ).StatusCode;

    [Fact]
    public async Task ChangeMyPassword_WithWrongCurrentPasswords_LocksTheAccountUntilTheLockoutEnds()
    {
        using WebApplicationFactory<Program> host = CreateHost();
        Account account = await CreateAccountAsync(host);

        for (int i = 1; i < MaxAttempts; i++)
        {
            HttpResponseMessage wrong = await ChangePasswordAsync(account, "Wrong-Password-1!");
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
            Assert.Contains("PasswordMismatch", await wrong.Content.ReadAsStringAsync());
        }

        // The attempt that reaches the limit is already answered as locked out.
        HttpResponseMessage locked = await ChangePasswordAsync(account, "Wrong-Password-1!");
        Assert.Equal(HttpStatusCode.Locked, locked.StatusCode);

        // The correct password is refused too, and the password is unchanged.
        Assert.False((await ChangePasswordAsync(account, Password)).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Locked, await LoginStatusAsync(host, account, Password));

        await EndLockoutAsync(host, account.Id);
        Assert.Equal(HttpStatusCode.OK, await LoginStatusAsync(host, account, Password));
    }

    [Fact]
    public async Task ChangeMyEmail_WithWrongCurrentPasswords_LocksTheAccountUntilTheLockoutEnds()
    {
        using WebApplicationFactory<Program> host = CreateHost();
        Account account = await CreateAccountAsync(host);

        for (int i = 1; i < MaxAttempts; i++)
        {
            HttpResponseMessage wrong = await ChangeEmailAsync(account, "Wrong-Password-1!");
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
            Assert.Contains("PasswordMismatch", await wrong.Content.ReadAsStringAsync());
        }

        HttpResponseMessage locked = await ChangeEmailAsync(account, "Wrong-Password-1!");
        Assert.Equal(HttpStatusCode.Locked, locked.StatusCode);

        string before = (await ReadUserAsync(host, account.Id)).Email;
        Assert.False((await ChangeEmailAsync(account, Password)).IsSuccessStatusCode);
        Assert.Equal(before, (await ReadUserAsync(host, account.Id)).Email);
        Assert.Equal(HttpStatusCode.Locked, await LoginStatusAsync(host, account, Password));

        await EndLockoutAsync(host, account.Id);
        Assert.Equal(HttpStatusCode.OK, await LoginStatusAsync(host, account, Password));
    }

    [Fact]
    public async Task ChangeMyEmail_WithTheCorrectPassword_ResetsTheFailedCount()
    {
        using WebApplicationFactory<Program> host = CreateHost();
        Account account = await CreateAccountAsync(host);

        for (int i = 1; i < MaxAttempts; i++)
            Assert.Equal(
                HttpStatusCode.BadRequest,
                (await ChangeEmailAsync(account, "Wrong-Password-1!")).StatusCode
            );
        Assert.Equal(MaxAttempts - 1, (await ReadUserAsync(host, account.Id)).AccessFailedCount);

        Assert.Equal(HttpStatusCode.OK, (await ChangeEmailAsync(account, Password)).StatusCode);
        Assert.Equal(0, (await ReadUserAsync(host, account.Id)).AccessFailedCount);
    }
}
