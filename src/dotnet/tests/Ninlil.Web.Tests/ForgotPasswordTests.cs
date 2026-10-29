using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the anonymous password-recovery request
///     (<see cref="AuthController.ForgotPassword" />) over HTTP on the shared in-process host. Its
///     main concern is account enumeration: a registered address, an unknown one, an empty one
///     and a disabled account must all get the same status and body. That the response does not
///     wait for SMTP, and how the link is built, is covered by <see cref="PasswordResetLinkTests" />.
///     <para>
///         The shared host has no SMTP server configured, as
///         <c>PasswordChangeAndResetTests.AdminResetPassword_EmailLink_WithNoSmtpConfigured_...</c>
///         also relies on. A registered address therefore also checks that the action skips the
///         send when SMTP is not set up instead of letting <c>SmtpClient</c> throw and return a
///         <c>500</c>. Whether an e-mail is sent when SMTP is configured is tested in
///         <see cref="PasswordResetLinkTests" />.
///     </para>
/// </summary>
[Collection(WebTestCollection.Name)]
public class ForgotPasswordTests(NinlilDnsWebApplicationFactory factory)
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
    ///     This method creates a new user with no permissions and a unique e-mail address through
    ///     the admin API, so the tests never touch the bootstrap administrator.
    /// </summary>
    private async Task<(Guid Id, string UserName, string Email)> CreateOrdinaryUserAsync(
        string suffix
    )
    {
        HttpClient adminClient = await CreateAuthenticatedAdminClientAsync();

        string userName = $"forgot-pwd-test-user-{suffix}";
        string email = $"{userName}@localhost.local";
        const string password = "Original-Password-1234!";

        HttpResponseMessage createResponse = await adminClient.PostAsJsonAsync(
            "/api/users",
            new CreateUserRequest(userName, password, email, "Forgot Password Test User")
        );
        createResponse.EnsureSuccessStatusCode();
        UserSummary created = await createResponse.Content.ReadFromJsonAsync<UserSummary>();

        return (created.Id, userName, email);
    }

    [Fact]
    public async Task ForgotPassword_WithARegisteredEmail_ReturnsTheGenericOkResponse()
    {
        (_, _, string email) = await CreateOrdinaryUserAsync(Guid.NewGuid().ToString("N"));

        HttpResponseMessage response = await CreateClient()
            .PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ForgotPasswordResult result =
            await response.Content.ReadFromJsonAsync<ForgotPasswordResult>();
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public async Task ForgotPassword_WithAnUnregisteredEmail_ReturnsTheIdenticalGenericOkResponse()
    {
        HttpResponseMessage knownResponse;
        {
            (_, _, string email) = await CreateOrdinaryUserAsync(Guid.NewGuid().ToString("N"));
            knownResponse = await CreateClient()
                .PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email));
        }

        HttpResponseMessage unknownResponse = await CreateClient()
            .PostAsJsonAsync(
                "/api/auth/forgot-password",
                new ForgotPasswordRequest("definitely-not-a-real-account@localhost.local")
            );

        // Status code and body must be identical whether the address exists or not, so a caller
        // cannot tell the two apart from the response.
        Assert.Equal(knownResponse.StatusCode, unknownResponse.StatusCode);
        string knownBody = await knownResponse.Content.ReadAsStringAsync();
        string unknownBody = await unknownResponse.Content.ReadAsStringAsync();
        Assert.Equal(knownBody, unknownBody);
    }

    [Fact]
    public async Task ForgotPassword_WithAMalformedOrEmptyEmail_StillReturnsTheGenericOkResponse()
    {
        HttpResponseMessage response = await CreateClient()
            .PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        HttpResponseMessage malformedResponse = await CreateClient()
            .PostAsJsonAsync(
                "/api/auth/forgot-password",
                new ForgotPasswordRequest("not-an-email-address")
            );

        Assert.Equal(HttpStatusCode.OK, malformedResponse.StatusCode);
        Assert.Equal(
            await response.Content.ReadAsStringAsync(),
            await malformedResponse.Content.ReadAsStringAsync()
        );
    }

    [Fact]
    public async Task ForgotPassword_ForADisabledAccount_StillReturnsTheGenericOkResponse()
    {
        (Guid id, string userName, string email) = await CreateOrdinaryUserAsync(
            Guid.NewGuid().ToString("N")
        );
        HttpClient adminClient = await CreateAuthenticatedAdminClientAsync();

        // Disable the account; a disabled account must get the same response as an unknown one.
        // Update is a full replace, so the e-mail is sent again (and the display name becomes
        // the user name) along with the flag.
        HttpResponseMessage updateResponse = await adminClient.PutAsJsonAsync(
            $"/api/users/{id}",
            new UpdateUserRequest(userName, email, true)
        );
        updateResponse.EnsureSuccessStatusCode();

        HttpResponseMessage response = await CreateClient()
            .PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ForgotPassword_RequiresNoAuthentication_AnAnonymousCallerSucceeds()
    {
        // A client with no bearer token: a signed-out visitor must be able to use this action.
        HttpResponseMessage response = await CreateClient()
            .PostAsJsonAsync(
                "/api/auth/forgot-password",
                new ForgotPasswordRequest("anonymous-caller-check@localhost.local")
            );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
