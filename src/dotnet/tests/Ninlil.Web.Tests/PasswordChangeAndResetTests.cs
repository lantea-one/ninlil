using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers three password-management flows against a real,
///     in-process host and a real SQLite database (see
///     <see cref="NinlilDnsWebApplicationFactory" />): self-service change-password
///     (<see cref="UsersController.ChangeMyPassword" />), the administrator-driven reset
///     (<see cref="UsersController.ResetPassword" />, both delivery modes), and the shared
///     token-based confirm-reset action every "set new password" landing page submits to
///     (<see cref="AuthController.ResetPassword" />). Every "the new password actually works"
///     assertion below signs in for real with it (and confirms the old password no longer signs
///     in), rather than trusting a bare <c>200</c>/<c>204</c> status code alone.
/// </summary>
[Collection(WebTestCollection.Name)]
public class PasswordChangeAndResetTests(NinlilDnsWebApplicationFactory factory)
{
    private HttpClient CreateClient() => factory.CreateClient();

    private async Task<HttpClient> CreateAuthenticatedAdminClientAsync()
    {
        HttpClient client = CreateClient();

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

    /// <summary>
    ///     This method creates a fresh, dedicated, ordinary (non-administrative) user account via
    ///     the admin API, and returns both its identifier/user name/initial password and a client
    ///     already authenticated as it -- reused by every test below that needs its own account,
    ///     never the shared bootstrap administrator, so a password change in one test can never
    ///     affect another sharing this fixture.
    /// </summary>
    private async Task<(
        HttpClient Client,
        Guid Id,
        string UserName,
        string Password
    )> CreateOrdinaryUserClientAsync(string suffix)
    {
        HttpClient adminClient = await CreateAuthenticatedAdminClientAsync();

        string userName = $"pwd-test-user-{suffix}";
        string email = $"{userName}@localhost.local";
        const string password = "Original-Password-1234!";

        HttpResponseMessage createResponse = await adminClient.PostAsJsonAsync(
            "/api/users",
            new CreateUserRequest(userName, password, email, "Password Test User")
        );
        createResponse.EnsureSuccessStatusCode();
        UserSummary created = await createResponse.Content.ReadFromJsonAsync<UserSummary>();

        HttpClient userClient = await SignInAsAsync(userName, password);

        return (userClient, created.Id, userName, password);
    }

    private async Task<HttpClient> SignInAsAsync(string userName, string password)
    {
        HttpClient client = CreateClient();
        HttpResponseMessage loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(userName, password)
        );
        loginResponse.EnsureSuccessStatusCode();
        LoginResponse login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            login.AccessToken
        );
        return client;
    }

    private async Task<bool> CanSignInAsAsync(string userName, string password)
    {
        HttpResponseMessage response = await CreateClient()
            .PostAsJsonAsync("/api/auth/login", new LoginRequest(userName, password));
        return response.StatusCode == HttpStatusCode.OK;
    }

    [Fact]
    public async Task ChangeMyPassword_WithCorrectCurrentPassword_ChangesIt()
    {
        (HttpClient client, _, string userName, string password) =
            await CreateOrdinaryUserClientAsync(Guid.NewGuid().ToString("N"));
        const string newPassword = "Brand-New-Password-5678!";

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/users/me/change-password",
            new ChangePasswordRequest(password, newPassword)
        );

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // The new password genuinely signs in...
        Assert.True(await CanSignInAsAsync(userName, newPassword));

        // ...and the old one genuinely no longer does.
        Assert.False(await CanSignInAsAsync(userName, password));
    }

    [Fact]
    public async Task ChangeMyPassword_WithWrongCurrentPassword_ReturnsARealValidationErrorAndLeavesPasswordUnchanged()
    {
        (HttpClient client, _, string userName, string password) =
            await CreateOrdinaryUserClientAsync(Guid.NewGuid().ToString("N"));

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/users/me/change-password",
            new ChangePasswordRequest("Totally-Wrong-Password-0000!", "Brand-New-Password-5678!")
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Identity's own real PasswordMismatch failure must surface, not a swallowed generic
        // message -- ThrowIfFailed keys every ValidationException error by IdentityError.Code.
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("PasswordMismatch", body, StringComparison.OrdinalIgnoreCase);

        // The account's real password must be completely unaffected by the failed attempt.
        Assert.True(await CanSignInAsAsync(userName, password));
    }

    [Fact]
    public async Task ChangeMyPassword_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/users/me/change-password",
            new ChangePasswordRequest("whatever", "Brand-New-Password-5678!")
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdminResetPassword_AutoGenerate_ReturnsAWorkingPasswordOnceAndInvalidatesTheOldOne()
    {
        (_, Guid id, string userName, string oldPassword) = await CreateOrdinaryUserClientAsync(
            Guid.NewGuid().ToString("N")
        );
        HttpClient adminClient = await CreateAuthenticatedAdminClientAsync();

        HttpResponseMessage response = await adminClient.PostAsJsonAsync(
            $"/api/users/{id}/reset-password",
            new AdminResetPasswordRequest(AdminPasswordResetMode.AutoGenerate)
        );
        response.EnsureSuccessStatusCode();

        AdminResetPasswordResult result =
            await response.Content.ReadFromJsonAsync<AdminResetPasswordResult>();

        Assert.Equal(AdminPasswordResetMode.AutoGenerate, result.Mode);
        Assert.False(string.IsNullOrWhiteSpace(result.GeneratedPassword));

        // The generated password genuinely signs in...
        Assert.True(await CanSignInAsAsync(userName, result.GeneratedPassword));

        // ...and the account's old password genuinely no longer does.
        Assert.False(await CanSignInAsAsync(userName, oldPassword));
    }

    [Fact]
    public async Task AdminResetPassword_RequiresUsersWritePermission_AnOrdinaryCallerIsForbidden()
    {
        (HttpClient ordinaryClient, Guid id, _, _) = await CreateOrdinaryUserClientAsync(
            Guid.NewGuid().ToString("N")
        );

        HttpResponseMessage response = await ordinaryClient.PostAsJsonAsync(
            $"/api/users/{id}/reset-password",
            new AdminResetPasswordRequest(AdminPasswordResetMode.AutoGenerate)
        );

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminResetPassword_ForAnUnknownAccount_ReturnsNotFound()
    {
        HttpClient adminClient = await CreateAuthenticatedAdminClientAsync();

        HttpResponseMessage response = await adminClient.PostAsJsonAsync(
            $"/api/users/{Guid.NewGuid()}/reset-password",
            new AdminResetPasswordRequest(AdminPasswordResetMode.AutoGenerate)
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AdminResetPassword_EmailLink_WithNoSmtpConfigured_ReturnsAClearValidationErrorRatherThanARawSmtpFailure()
    {
        // This test collection's own shared host never configures an SMTP server (see
        // NinlilDnsWebApplicationFactory's own doc comment) -- exactly the common, real state
        // this project's homelab-first deployment default leaves an install in until an operator
        // deliberately sets one up via SmtpSettingsController, so this is a genuine, not merely
        // simulated, "SMTP not configured" state.
        (_, Guid id, _, _) = await CreateOrdinaryUserClientAsync(Guid.NewGuid().ToString("N"));
        HttpClient adminClient = await CreateAuthenticatedAdminClientAsync();

        HttpResponseMessage response = await adminClient.PostAsJsonAsync(
            $"/api/users/{id}/reset-password",
            new AdminResetPasswordRequest(AdminPasswordResetMode.EmailLink)
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("SMTP", body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     This test proves the shared "set new password" landing page's own confirming endpoint
    ///     (<see cref="AuthController.ResetPassword" />) for real, end to end: a real Identity
    ///     reset token is generated directly through <see cref="UserManager{TUser}" /> (reached
    ///     via this factory's own DI container, the same real path
    ///     <see cref="UsersController.ResetPassword" />'s own <c>EmailLink</c> mode uses --
    ///     standing in for actually receiving the e-mail, since this test collection's own shared
    ///     host has no SMTP server to send or receive through), Base64Url-encoded exactly the way
    ///     the controller encodes it for a URL query string, then submitted to the real,
    ///     unauthenticated confirm endpoint -- and the resulting new password is proven to
    ///     actually sign in afterward, with the old password proven to no longer work.
    /// </summary>
    [Fact]
    public async Task ConfirmPasswordReset_WithARealGeneratedToken_SetsANewWorkingPassword()
    {
        (_, Guid id, string userName, string oldPassword) = await CreateOrdinaryUserClientAsync(
            Guid.NewGuid().ToString("N")
        );

        string encodedToken = await GenerateEncodedResetTokenAsync(id);
        const string newPassword = "Confirmed-Reset-Password-9999!";

        HttpClient anonymousClient = CreateClient();
        HttpResponseMessage response = await anonymousClient.PostAsJsonAsync(
            "/api/auth/reset-password",
            new ConfirmPasswordResetRequest(id, encodedToken, newPassword)
        );

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        Assert.True(await CanSignInAsAsync(userName, newPassword));
        Assert.False(await CanSignInAsAsync(userName, oldPassword));
    }

    [Fact]
    public async Task ConfirmPasswordReset_WithATamperedToken_ReturnsAValidationErrorAndLeavesThePasswordUnchanged()
    {
        (_, Guid id, string userName, string oldPassword) = await CreateOrdinaryUserClientAsync(
            Guid.NewGuid().ToString("N")
        );

        string encodedToken = await GenerateEncodedResetTokenAsync(id);
        // Flip a character so the decoded token no longer matches anything Identity issued.
        char[] tamperedChars = encodedToken.ToCharArray();
        tamperedChars[0] = tamperedChars[0] == 'A' ? 'B' : 'A';
        string tamperedToken = new(tamperedChars);

        HttpResponseMessage response = await CreateClient()
            .PostAsJsonAsync(
                "/api/auth/reset-password",
                new ConfirmPasswordResetRequest(id, tamperedToken, "Should-Never-Apply-1234!")
            );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await CanSignInAsAsync(userName, oldPassword));
    }

    [Fact]
    public async Task ConfirmPasswordReset_ForAnUnknownAccount_ReturnsAGenericValidationErrorNotFound()
    {
        // Deliberately not 404 -- see AuthController.ResetPassword's own doc comment on why a
        // missing account and an unparseable/invalid token both report the exact same generic
        // message, so this action never confirms or denies whether a given account exists.
        HttpResponseMessage response = await CreateClient()
            .PostAsJsonAsync(
                "/api/auth/reset-password",
                new ConfirmPasswordResetRequest(
                    Guid.NewGuid(),
                    "not-a-real-token",
                    "Whatever-Password-1234!"
                )
            );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    ///     This method reaches into this factory's own real DI container to generate a real
    ///     Identity password-reset token for <paramref name="userId" />, then Base64Url-encodes it
    ///     exactly the way <see cref="UsersController.ResetPassword" />'s own <c>EmailLink</c>
    ///     delivery mode encodes it before placing it in a URL query string.
    /// </summary>
    private async Task<string> GenerateEncodedResetTokenAsync(Guid userId)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        UserManager<UserEntity> userManager = scope.ServiceProvider.GetRequiredService<
            UserManager<UserEntity>
        >();

        UserEntity user = await userManager.FindByIdAsync(userId.ToString());
        string rawToken = await userManager.GeneratePasswordResetTokenAsync(user);

        return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(rawToken));
    }
}
