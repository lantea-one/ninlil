using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers what a password change or a two-factor change does to the sessions of an
///     account, over HTTP on the shared in-process host. Each test makes a new user, signs it in
///     twice (a "stolen" session and the session that makes the change), makes the change, and
///     checks that the access token and the refresh token of both sessions are refused at once and
///     that a new sign-in works. The paths covered are the self-service password change, the
///     administrator reset, the token-based reset behind the Forgot password page, and
///     authenticator confirmation, turning two-factor off and new recovery codes. Starting an
///     authenticator setup is checked to leave the session alone, because the confirmation step
///     that follows needs it. <c>GET /api/users/me</c> is the probe for "the access token still
///     works".
/// </summary>
[Collection(WebTestCollection.Name)]
public class SecurityStampSignOutTests(NinlilDnsWebApplicationFactory factory)
{
    private const string Password = "Original-Password-1234!";
    private const string NewPassword = "Brand-New-Password-5678!";
    private const string Probe = "/api/users/me";
    private const string TwoFactorBase = "/api/auth/two-factor";

    /// <summary>
    ///     This record holds one signed-in session: a client that sends the access token and the
    ///     refresh token the sign-in returned in its body.
    /// </summary>
    private sealed record Session(HttpClient Client, string RefreshToken);

    private async Task<HttpClient> SignInAdminAsync()
    {
        Session admin = await SignInAsync(
            NinlilDnsWebApplicationFactory.AdminUserName,
            NinlilDnsWebApplicationFactory.AdminPassword
        );
        return admin.Client;
    }

    private async Task<Session> SignInAsync(string userName, string password)
    {
        HttpClient client = factory.CreateClient();
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
        return new Session(client, body.RefreshToken);
    }

    /// <summary>
    ///     This method creates a user without permissions and returns its id and user name.
    /// </summary>
    private async Task<(Guid Id, string UserName)> CreateUserAsync()
    {
        HttpClient admin = await SignInAdminAsync();
        string userName = $"stamp-{Guid.NewGuid():N}";
        HttpResponseMessage created = await admin.PostAsJsonAsync(
            "/api/users",
            new CreateUserRequest(userName, Password, $"{userName}@localhost.local", "Stamp Test")
        );
        created.EnsureSuccessStatusCode();
        return ((await created.Content.ReadFromJsonAsync<UserSummary>()).Id, userName);
    }

    private static async Task<HttpStatusCode> ProbeAsync(Session session) =>
        (await session.Client.GetAsync(Probe)).StatusCode;

    private async Task<HttpStatusCode> RefreshAsync(Session session) =>
        (
            await factory
                .CreateClient()
                .PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(session.RefreshToken))
        ).StatusCode;

    /// <summary>
    ///     This method asserts that a session is cut off: its access token and its refresh token
    ///     are both refused.
    /// </summary>
    private async Task AssertCutOffAsync(Session session)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await ProbeAsync(session));
        Assert.Equal(HttpStatusCode.Unauthorized, await RefreshAsync(session));
    }

    /// <summary>
    ///     This method asserts that a session still works: its access token is accepted.
    /// </summary>
    private static async Task AssertWorksAsync(Session session) =>
        Assert.Equal(HttpStatusCode.OK, await ProbeAsync(session));

    /// <summary>
    ///     This method sets up and confirms an authenticator through a session and returns the
    ///     secret. That confirmation ends the session, so the caller signs in again.
    /// </summary>
    private static async Task<string> EnrollAsync(Session session)
    {
        HttpResponseMessage setup = await session.Client.PostAsync(
            $"{TwoFactorBase}/authenticator/setup",
            null
        );
        setup.EnsureSuccessStatusCode();
        string secret = (
            await setup.Content.ReadFromJsonAsync<AuthenticatorSetupResult>()
        ).SharedKey;
        HttpResponseMessage enable = await session.Client.PostAsJsonAsync(
            $"{TwoFactorBase}/authenticator/enable",
            new TwoFactorCodeRequest(TotpGenerator.GenerateCode(secret))
        );
        Assert.Equal(HttpStatusCode.NoContent, enable.StatusCode);
        return secret;
    }

    /// <summary>
    ///     This method signs a user with two-factor sign-in on in again: the password step, then
    ///     the authenticator code.
    /// </summary>
    private async Task<Session> SignInWithTwoFactorAsync(string userName, string secret)
    {
        HttpClient client = factory.CreateClient();
        LoginResponse first = await (
            await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(userName, Password))
        ).Content.ReadFromJsonAsync<LoginResponse>();
        Assert.True(first.RequiresTwoFactor);
        HttpResponseMessage second = await client.PostAsJsonAsync(
            "/api/auth/login/two-factor",
            new TwoFactorLoginRequest(
                first.MfaToken,
                TotpGenerator.GenerateCode(secret),
                "Authenticator"
            )
        );
        second.EnsureSuccessStatusCode();
        LoginResponse body = await second.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            body.AccessToken
        );
        return new Session(client, body.RefreshToken);
    }

    [Fact]
    public async Task ChangingThePassword_CutsOffEverySessionIncludingTheCallers_AndANewSignInWorks()
    {
        (_, string userName) = await CreateUserAsync();
        Session other = await SignInAsync(userName, Password);
        Session caller = await SignInAsync(userName, Password);
        await AssertWorksAsync(other);
        await AssertWorksAsync(caller);

        HttpResponseMessage change = await caller.Client.PostAsJsonAsync(
            "/api/users/me/change-password",
            new ChangePasswordRequest(Password, NewPassword)
        );
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        await AssertCutOffAsync(other);
        await AssertCutOffAsync(caller);
        await AssertWorksAsync(await SignInAsync(userName, NewPassword));
    }

    [Fact]
    public async Task AnAdministratorPasswordReset_CutsOffEverySessionOfTheAccount()
    {
        (Guid id, string userName) = await CreateUserAsync();
        Session session = await SignInAsync(userName, Password);
        HttpClient admin = await SignInAdminAsync();

        HttpResponseMessage reset = await admin.PostAsJsonAsync(
            $"/api/users/{id}/reset-password",
            new AdminResetPasswordRequest(AdminPasswordResetMode.AutoGenerate)
        );
        reset.EnsureSuccessStatusCode();

        await AssertCutOffAsync(session);
    }

    [Fact]
    public async Task AResetWithAPasswordResetToken_CutsOffEverySessionOfTheAccount()
    {
        (Guid id, string userName) = await CreateUserAsync();
        Session session = await SignInAsync(userName, Password);
        string encodedToken;
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            UserManager<UserEntity> userManager = scope.ServiceProvider.GetRequiredService<
                UserManager<UserEntity>
            >();
            UserEntity user = await userManager.FindByIdAsync(id.ToString());
            encodedToken = WebEncoders.Base64UrlEncode(
                Encoding.UTF8.GetBytes(await userManager.GeneratePasswordResetTokenAsync(user))
            );
        }

        HttpResponseMessage reset = await factory
            .CreateClient()
            .PostAsJsonAsync(
                "/api/auth/reset-password",
                new ConfirmPasswordResetRequest(id, encodedToken, NewPassword)
            );
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        await AssertCutOffAsync(session);
    }

    [Fact]
    public async Task StartingAnAuthenticatorSetup_LeavesTheSessionAlone()
    {
        (_, string userName) = await CreateUserAsync();
        Session session = await SignInAsync(userName, Password);

        HttpResponseMessage setup = await session.Client.PostAsync(
            $"{TwoFactorBase}/authenticator/setup",
            null
        );
        setup.EnsureSuccessStatusCode();

        await AssertWorksAsync(session);
        Assert.Equal(HttpStatusCode.OK, await RefreshAsync(session));
    }

    [Fact]
    public async Task ConfirmingAnAuthenticator_CutsOffEverySessionIncludingTheCallers()
    {
        (_, string userName) = await CreateUserAsync();
        Session other = await SignInAsync(userName, Password);
        Session caller = await SignInAsync(userName, Password);

        string secret = await EnrollAsync(caller);

        await AssertCutOffAsync(other);
        await AssertCutOffAsync(caller);
        await AssertWorksAsync(await SignInWithTwoFactorAsync(userName, secret));
    }

    [Fact]
    public async Task TurningTwoFactorOff_CutsOffEverySessionIncludingTheCallers()
    {
        (_, string userName) = await CreateUserAsync();
        string secret = await EnrollAsync(await SignInAsync(userName, Password));
        Session other = await SignInWithTwoFactorAsync(userName, secret);
        Session caller = await SignInWithTwoFactorAsync(userName, secret);

        HttpResponseMessage disable = await caller.Client.PostAsJsonAsync(
            $"{TwoFactorBase}/disable",
            new TwoFactorCodeRequest(TotpGenerator.GenerateCode(secret))
        );
        Assert.Equal(HttpStatusCode.NoContent, disable.StatusCode);

        await AssertCutOffAsync(other);
        await AssertCutOffAsync(caller);
        await AssertWorksAsync(await SignInAsync(userName, Password));
    }

    [Fact]
    public async Task NewRecoveryCodes_CutOffEverySessionAfterReturningTheCodes()
    {
        (_, string userName) = await CreateUserAsync();
        string secret = await EnrollAsync(await SignInAsync(userName, Password));
        Session other = await SignInWithTwoFactorAsync(userName, secret);
        Session caller = await SignInWithTwoFactorAsync(userName, secret);

        HttpResponseMessage generated = await caller.Client.PostAsync(
            $"{TwoFactorBase}/recovery-codes/generate",
            null
        );
        generated.EnsureSuccessStatusCode();
        RecoveryCodesResult codes =
            await generated.Content.ReadFromJsonAsync<RecoveryCodesResult>();
        Assert.NotEmpty(codes.Codes);

        await AssertCutOffAsync(other);
        await AssertCutOffAsync(caller);
    }

    [Fact]
    public async Task ReplacingAnActiveAuthenticator_KeepsTheSessionUntilTheNewKeyIsConfirmed()
    {
        (_, string userName) = await CreateUserAsync();
        string secret = await EnrollAsync(await SignInAsync(userName, Password));
        Session session = await SignInWithTwoFactorAsync(userName, secret);

        HttpResponseMessage setup = await session.Client.PostAsJsonAsync(
            $"{TwoFactorBase}/authenticator/setup",
            new TwoFactorCodeRequest(TotpGenerator.GenerateCode(secret))
        );
        setup.EnsureSuccessStatusCode();
        string replaced = (
            await setup.Content.ReadFromJsonAsync<AuthenticatorSetupResult>()
        ).SharedKey;
        await AssertWorksAsync(session);

        HttpResponseMessage enable = await session.Client.PostAsJsonAsync(
            $"{TwoFactorBase}/authenticator/enable",
            new TwoFactorCodeRequest(TotpGenerator.GenerateCode(replaced))
        );
        Assert.Equal(HttpStatusCode.NoContent, enable.StatusCode);
        await AssertCutOffAsync(session);
    }

    [Fact]
    public async Task AnOrdinaryProfileEdit_DoesNotCutOffTheSession()
    {
        (_, string userName) = await CreateUserAsync();
        Session session = await SignInAsync(userName, Password);

        using HttpRequestMessage patch = new(HttpMethod.Patch, Probe)
        {
            Content = new StringContent(
                "{\"displayName\":\"Renamed\"}",
                Encoding.UTF8,
                "application/merge-patch+json"
            ),
        };
        HttpResponseMessage edit = await session.Client.SendAsync(patch);
        Assert.True(edit.IsSuccessStatusCode);

        await AssertWorksAsync(session);
        Assert.Equal(HttpStatusCode.OK, await RefreshAsync(session));
    }
}
