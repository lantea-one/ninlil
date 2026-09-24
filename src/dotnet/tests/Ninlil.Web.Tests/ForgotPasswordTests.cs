using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the genuinely anonymous self-service password-recovery
///     request (<see cref="AuthController.ForgotPassword" />) against a real, in-process host and a
///     real SQLite database (see <see cref="NinlilDnsWebApplicationFactory" />). Its central
///     concern is the account-enumeration property that action's own doc comment calls out as
///     security-critical: a real, registered e-mail address and a completely unknown one must
///     produce the exact same response, byte for byte, with no observable difference a caller could
///     use to tell "an account exists here" from "it doesn't."
///     <para>
///         This test collection's own shared host never configures an SMTP server (see
///         <see cref="NinlilDnsWebApplicationFactory" />'s own doc comment on why -- the same
///         real, common "SMTP never set up" state <c>PasswordChangeAndResetTests</c>'s own
///         <c>AdminResetPassword_EmailLink_WithNoSmtpConfigured_...</c> test already relies on).
///         That means every test below that submits a real, registered account's e-mail address
///         also proves this action's own SMTP pre-flight guard fires silently (no exception, no
///         status-code difference, no observable effect at all) rather than letting an unconfigured
///         <c>SmtpClient</c> throw and surface as a real <c>500</c> -- if the guard were missing or
///         broken, these tests would fail with a server error instead of the expected <c>200</c>.
///         The real, literal proof that an e-mail genuinely does (or does not) leave this server --
///         a real outbound SMTP connection observed by a real catcher -- is covered separately by
///         a real end-to-end smoke test against a real SMTP catcher, not reproducible inside this
///         shared collection's SMTP-less host.
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
    ///     This method creates a fresh, dedicated, ordinary (non-administrative) user account with
    ///     a real, unique e-mail address, via the admin API -- never the shared bootstrap
    ///     administrator, so a test in this class never affects another sharing this fixture.
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

        // The whole point of this action: status code and response body must be byte-for-byte
        // identical whether the address exists or not, so a caller can never tell the two apart.
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
    }

    [Fact]
    public async Task ForgotPassword_ForADisabledAccount_StillReturnsTheGenericOkResponse()
    {
        (Guid id, string userName, string email) = await CreateOrdinaryUserAsync(
            Guid.NewGuid().ToString("N")
        );
        HttpClient adminClient = await CreateAuthenticatedAdminClientAsync();

        // Disable the account -- a disabled account must be treated exactly like an unknown one by
        // this action, never surfacing a different response. Update is a full replace, so the
        // account's own real display name/e-mail are echoed back unchanged alongside the flag.
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
        // Deliberately using a bare, never-authenticated HttpClient (no bearer token at all) --
        // this action must be reachable by a completely signed-out visitor, the whole premise of
        // a self-service Forgot Password flow.
        HttpResponseMessage response = await CreateClient()
            .PostAsJsonAsync(
                "/api/auth/forgot-password",
                new ForgotPasswordRequest("anonymous-caller-check@localhost.local")
            );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
