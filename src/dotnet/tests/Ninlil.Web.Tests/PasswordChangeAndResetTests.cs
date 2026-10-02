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
///     This class covers three password flows over HTTP on the shared in-process host, each on a
///     new user with no permissions: changing one's own password
///     (<see cref="UsersController.ChangeMyPassword" />), the administrator reset
///     (<see cref="UsersController.ResetPassword" />, both delivery modes, signed in as the
///     bootstrap administrator), and the token-based confirm action the "set new password" page
///     submits to (<see cref="AuthController.ResetPassword" />). A changed password is checked by
///     signing in with it and failing to sign in with the old one, not by the status code alone.
///     Whether existing refresh tokens stop working after a change is not tested.
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
    ///     This method creates a new user with no permissions through the admin API and returns
    ///     its identifier, user name and initial password, with a client signed in as it, so
    ///     password changes never touch the bootstrap administrator other tests sign in as.
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

        // The new password signs in...
        Assert.True(await CanSignInAsAsync(userName, newPassword));

        // ...and the old one no longer does.
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

        // Identity's PasswordMismatch error must come through rather than a generic message;
        // ThrowIfFailed keys each ValidationException error by IdentityError.Code.
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("PasswordMismatch", body, StringComparison.OrdinalIgnoreCase);

        // The failed attempt must leave the password unchanged.
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

        // The generated password signs in...
        Assert.True(await CanSignInAsAsync(userName, result.GeneratedPassword));

        // ...and the old one no longer does.
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
        // The shared host has no SMTP server configured, the default state of an installation
        // until an operator sets one up through SmtpSettingsController.
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
    ///     This test covers the confirm endpoint (<see cref="AuthController.ResetPassword" />). The
    ///     host has no SMTP server, so instead of receiving an e-mail the test gets a reset token
    ///     from the host's <see cref="UserManager{TUser}" />, as the <c>EmailLink</c> mode of
    ///     <see cref="UsersController.ResetPassword" /> does, encodes it as the controller does for
    ///     the link, and submits it without signing in. The new password must then sign in and the
    ///     old one must not.
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
        // Not 404: a missing account and an invalid token get the same generic message, so the
        // action never reveals whether an account exists (see AuthController.ResetPassword). Only
        // the status code is checked, not that the messages match.
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
    ///     This method generates an Identity password-reset token for <paramref name="userId" />
    ///     through the host's services and Base64Url-encodes it as the <c>EmailLink</c> mode of
    ///     <see cref="UsersController.ResetPassword" /> does before putting it in the link.
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
