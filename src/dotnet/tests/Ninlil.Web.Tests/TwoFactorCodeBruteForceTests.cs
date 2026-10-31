using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Web.Models;
using Ninlil.Web.Services;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the per-user limit on wrong second-factor codes over HTTP on the shared
///     in-process host, each test on a new user enrolled in TOTP. After five wrong codes the next
///     check answers 429 with <c>Retry-After</c> even for a correct code; the enable, disable and
///     re-setup checks and the sign-in's second step share one budget; another user is not
///     affected; and a throttled sign-in attempt does not spend the pending sign-in token. The
///     shared host disables the per-IP login limiter, so these tests exercise only the per-user
///     limit, which stays on whatever the per-IP setting says.
/// </summary>
[Collection(WebTestCollection.Name)]
public class TwoFactorCodeBruteForceTests(NinlilDnsWebApplicationFactory factory)
{
    private const string SetupUrl = "/api/auth/two-factor/authenticator/setup";
    private const string EnableUrl = "/api/auth/two-factor/authenticator/enable";
    private const string DisableUrl = "/api/auth/two-factor/disable";
    private const string TwoFactorLoginUrl = "/api/auth/login/two-factor";

    private const string Password = "Brute-Force-Password-1234!";

    private sealed record EnrolledUser(
        HttpClient Client,
        string UserName,
        string Secret,
        Guid UserId
    );

    /// <summary>
    ///     This method creates a new user, signs in as it and enrolls it in TOTP.
    /// </summary>
    private async Task<EnrolledUser> CreateEnrolledUserAsync()
    {
        HttpClient adminClient = factory.CreateClient();
        HttpResponseMessage adminLogin = await adminClient.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(
                NinlilDnsWebApplicationFactory.AdminUserName,
                NinlilDnsWebApplicationFactory.AdminPassword
            )
        );
        adminLogin.EnsureSuccessStatusCode();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            (await adminLogin.Content.ReadFromJsonAsync<LoginResponse>()).AccessToken
        );

        string userName = $"totp-brute-{Guid.NewGuid():N}";
        HttpResponseMessage created = await adminClient.PostAsJsonAsync(
            "/api/users",
            new CreateUserRequest(userName, Password, $"{userName}@localhost.local", "Brute Force")
        );
        created.EnsureSuccessStatusCode();
        Guid userId = (await created.Content.ReadFromJsonAsync<UserSummary>()).Id;

        HttpClient client = factory.CreateClient();
        HttpResponseMessage login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(userName, Password)
        );
        login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            (await login.Content.ReadFromJsonAsync<LoginResponse>()).AccessToken
        );

        HttpResponseMessage setup = await client.PostAsync(SetupUrl, null);
        setup.EnsureSuccessStatusCode();
        string secret = (
            await setup.Content.ReadFromJsonAsync<AuthenticatorSetupResult>()
        ).SharedKey;
        (
            await client.PostAsJsonAsync(
                EnableUrl,
                new TwoFactorCodeRequest(TotpGenerator.GenerateCode(secret))
            )
        ).EnsureSuccessStatusCode();

        // The enrollment succeeded, so the budget starts empty. Confirming the authenticator
        // ended the session above, so the user gets a new one by signing in with the code, which
        // also leaves the budget empty.
        string mfaToken = await StartTwoFactorSignInAsync(userName);
        HttpResponseMessage second = await SubmitSignInCodeAsync(
            mfaToken,
            TotpGenerator.GenerateCode(secret)
        );
        second.EnsureSuccessStatusCode();
        HttpClient signedIn = factory.CreateClient();
        signedIn.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            (await second.Content.ReadFromJsonAsync<LoginResponse>()).AccessToken
        );

        return new EnrolledUser(signedIn, userName, secret, userId);
    }

    private static string WrongCode(string secret)
    {
        string current = TotpGenerator.GenerateCode(secret);
        return current == "000000" ? "000001" : "000000";
    }

    private static Task<HttpResponseMessage> PostCodeAsync(
        HttpClient client,
        string url,
        string code
    ) => client.PostAsJsonAsync(url, new TwoFactorCodeRequest(code));

    /// <summary>
    ///     This method signs in with the password and returns the pending sign-in token.
    /// </summary>
    private async Task<string> StartTwoFactorSignInAsync(string userName)
    {
        HttpResponseMessage login = await factory
            .CreateClient()
            .PostAsJsonAsync("/api/auth/login", new LoginRequest(userName, Password));
        login.EnsureSuccessStatusCode();
        LoginResponse body = await login.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.True(body.RequiresTwoFactor);
        return body.MfaToken;
    }

    private Task<HttpResponseMessage> SubmitSignInCodeAsync(string mfaToken, string code) =>
        factory
            .CreateClient()
            .PostAsJsonAsync(
                TwoFactorLoginUrl,
                new TwoFactorLoginRequest(mfaToken, code, "Authenticator")
            );

    [Fact]
    public async Task FiveWrongCodesOnDisableThenACorrectCodeAnswers429WithRetryAfter()
    {
        EnrolledUser user = await CreateEnrolledUserAsync();

        for (int i = 0; i < 5; i++)
            Assert.Equal(
                HttpStatusCode.BadRequest,
                (await PostCodeAsync(user.Client, DisableUrl, WrongCode(user.Secret))).StatusCode
            );

        HttpResponseMessage blocked = await PostCodeAsync(
            user.Client,
            DisableUrl,
            TotpGenerator.GenerateCode(user.Secret)
        );

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.True(blocked.Headers.RetryAfter?.Delta > TimeSpan.Zero);
    }

    [Fact]
    public async Task AWrongCodeFollowedByACorrectCodeStillWorks()
    {
        EnrolledUser user = await CreateEnrolledUserAsync();

        for (int i = 0; i < 4; i++)
            await PostCodeAsync(user.Client, DisableUrl, WrongCode(user.Secret));
        HttpResponseMessage correct = await PostCodeAsync(
            user.Client,
            DisableUrl,
            TotpGenerator.GenerateCode(user.Secret)
        );

        Assert.Equal(HttpStatusCode.NoContent, correct.StatusCode);
    }

    [Fact]
    public async Task ASuccessClearsTheFailuresSoFiveMoreWrongCodesAreNeededToBlock()
    {
        EnrolledUser user = await CreateEnrolledUserAsync();

        // The re-setup check is the success that leaves the session in place: confirming and
        // turning off end it. A success replaces the secret, so the second round uses the new one.
        for (int i = 0; i < 4; i++)
            await PostCodeAsync(user.Client, SetupUrl, WrongCode(user.Secret));
        HttpResponseMessage first = await PostCodeAsync(
            user.Client,
            SetupUrl,
            TotpGenerator.GenerateCode(user.Secret)
        );
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        string replaced = (
            await first.Content.ReadFromJsonAsync<AuthenticatorSetupResult>()
        ).SharedKey;

        // Without the clear, these four wrong codes would be the eighth to ninth failure.
        for (int i = 0; i < 4; i++)
            await PostCodeAsync(user.Client, SetupUrl, WrongCode(replaced));

        Assert.Equal(
            HttpStatusCode.OK,
            (
                await PostCodeAsync(user.Client, SetupUrl, TotpGenerator.GenerateCode(replaced))
            ).StatusCode
        );
    }

    [Fact]
    public async Task EnableDisableAndReSetupShareOneBudget()
    {
        EnrolledUser user = await CreateEnrolledUserAsync();
        string wrong = WrongCode(user.Secret);

        await PostCodeAsync(user.Client, EnableUrl, wrong);
        await PostCodeAsync(user.Client, EnableUrl, wrong);
        await PostCodeAsync(user.Client, DisableUrl, wrong);
        await PostCodeAsync(user.Client, SetupUrl, wrong);
        await PostCodeAsync(user.Client, SetupUrl, wrong);

        foreach (string url in new[] { EnableUrl, DisableUrl, SetupUrl })
            Assert.Equal(
                HttpStatusCode.TooManyRequests,
                (
                    await PostCodeAsync(user.Client, url, TotpGenerator.GenerateCode(user.Secret))
                ).StatusCode
            );
    }

    [Fact]
    public async Task AnotherUserIsUnaffectedByTheBlock()
    {
        EnrolledUser blocked = await CreateEnrolledUserAsync();
        EnrolledUser other = await CreateEnrolledUserAsync();
        for (int i = 0; i < 5; i++)
            await PostCodeAsync(blocked.Client, DisableUrl, WrongCode(blocked.Secret));

        HttpResponseMessage response = await PostCodeAsync(
            other.Client,
            DisableUrl,
            TotpGenerator.GenerateCode(other.Secret)
        );

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task FiveWrongSignInCodesThenTheSixthAnswers429AndKeepsThePendingToken()
    {
        EnrolledUser user = await CreateEnrolledUserAsync();

        // Each wrong code spends its pending token, so every attempt starts with a fresh
        // password sign-in, each a separate client as an address-rotating guesser would use.
        for (int i = 0; i < 5; i++)
        {
            string token = await StartTwoFactorSignInAsync(user.UserName);
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await SubmitSignInCodeAsync(token, WrongCode(user.Secret))).StatusCode
            );
        }

        string sixthToken = await StartTwoFactorSignInAsync(user.UserName);
        HttpResponseMessage blocked = await SubmitSignInCodeAsync(
            sixthToken,
            TotpGenerator.GenerateCode(user.Secret)
        );

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.True(blocked.Headers.RetryAfter?.Delta > TimeSpan.Zero);

        // The throttled attempt did not spend the pending sign-in.
        Assert.NotNull(
            factory.Services.GetRequiredService<PendingTwoFactorSignInCache>().Peek(sixthToken)
        );
    }

    [Fact]
    public async Task WrongSignInCodesAlsoBlockTheManagementEndpoints()
    {
        EnrolledUser user = await CreateEnrolledUserAsync();
        for (int i = 0; i < 5; i++)
        {
            string token = await StartTwoFactorSignInAsync(user.UserName);
            await SubmitSignInCodeAsync(token, WrongCode(user.Secret));
        }

        HttpResponseMessage response = await PostCodeAsync(
            user.Client,
            DisableUrl,
            TotpGenerator.GenerateCode(user.Secret)
        );

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }
}
