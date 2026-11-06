using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Identity.Email;
using Ninlil.Dns.Identity.Options;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the self-service e-mail change over HTTP: <c>POST /api/users/me/email</c>
///     (<see cref="Controllers.UsersController.ChangeMyEmail" />) and
///     <c>POST /api/auth/confirm-email-change</c>
///     (<see cref="Controllers.AuthController.ConfirmEmailChange" />). Each test runs on a second
///     host from <see cref="NinlilDnsWebApplicationFactory.CreateSecondHost" /> that shares the
///     shared host's database but has its own e-mail settings, its own confirmation policy and a
///     recording <see cref="IEmailSender" />. The shared host is never changed, so it keeps the
///     default (confirmation on, no SMTP) for every other class.
/// </summary>
[Collection(WebTestCollection.Name)]
public class EmailChangeSelfServiceTests(NinlilDnsWebApplicationFactory factory)
{
    private const string Password = "Original-Password-1234!";
    private const string PublicBaseUrl = "https://dns.example.com/console";

    /// <summary>
    ///     This class records every message it is asked to send, so a test can read the
    ///     confirmation link and see which address got the notice.
    /// </summary>
    private sealed class RecordingEmailSender : IEmailSender
    {
        private readonly object _lock = new();
        private readonly List<(string To, string Subject, string Body)> _sent = [];

        /// <summary>The messages sent so far, as (address, subject, body).</summary>
        public IReadOnlyList<(string To, string Subject, string Body)> Sent
        {
            get
            {
                lock (_lock)
                    return [.. _sent];
            }
        }

        /// <inheritdoc />
        public Task SendAsync(
            string toAddress,
            string subject,
            string htmlBody,
            CancellationToken stoppingToken = default
        )
        {
            lock (_lock)
                _sent.Add((toAddress, subject, htmlBody));
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     This record is one account on the second host with a client signed in as it.
    /// </summary>
    private sealed record Account(
        Guid Id,
        string UserName,
        string Email,
        HttpClient Client,
        string AccessToken
    );

    /// <summary>
    ///     This method starts a second host with fixed e-mail settings and policy and the given
    ///     recording sender.
    /// </summary>
    private WebApplicationFactory<Program> CreateHost(
        RecordingEmailSender sender,
        bool requireConfirmation,
        string smtpHost = "smtp.example.invalid",
        string publicBaseUrl = PublicBaseUrl
    ) =>
        factory.CreateSecondHost(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(sender);
                services.Configure<EmailSenderOptions>(options =>
                {
                    options.SmtpHost = smtpHost;
                    options.FromAddress = "notifications@example.invalid";
                    options.PublicBaseUrl = publicBaseUrl;
                });
                services.Configure<EmailChangeOptions>(options =>
                    options.RequireConfirmation = requireConfirmation
                );
            })
        );

    private static async Task<(HttpClient Client, string AccessToken)> SignInAsync(
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
        return (client, body.AccessToken);
    }

    /// <summary>
    ///     This method creates an account without permissions through the admin API, optionally
    ///     marks its address confirmed, and signs in as it.
    /// </summary>
    private static async Task<Account> CreateAccountAsync(
        WebApplicationFactory<Program> host,
        bool emailConfirmed = false
    )
    {
        (HttpClient admin, _) = await SignInAsync(
            host,
            NinlilDnsWebApplicationFactory.AdminUserName,
            NinlilDnsWebApplicationFactory.AdminPassword
        );
        string userName = $"email-change-{Guid.NewGuid():N}";
        string email = $"{userName}@localhost.local";
        HttpResponseMessage created = await admin.PostAsJsonAsync(
            "/api/users",
            new CreateUserRequest(userName, Password, email, "Email Change Test")
        );
        created.EnsureSuccessStatusCode();
        Guid id = (await created.Content.ReadFromJsonAsync<UserSummary>()).Id;

        if (emailConfirmed)
        {
            await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
            UserManager<UserEntity> userManager = scope.ServiceProvider.GetRequiredService<
                UserManager<UserEntity>
            >();
            UserEntity user = await userManager.FindByIdAsync(id.ToString());
            user.EmailConfirmed = true;
            Assert.True((await userManager.UpdateAsync(user)).Succeeded);
        }

        (HttpClient client, string accessToken) = await SignInAsync(host, userName, Password);
        return new Account(id, userName, email, client, accessToken);
    }

    private static async Task<(string Email, bool EmailConfirmed)> ReadAddressAsync(
        WebApplicationFactory<Program> host,
        Guid id
    )
    {
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        UserEntity user = await scope
            .ServiceProvider.GetRequiredService<UserManager<UserEntity>>()
            .FindByIdAsync(id.ToString());
        return (user.Email, user.EmailConfirmed);
    }

    private static Task<HttpResponseMessage> ChangeEmailAsync(
        Account account,
        string newEmail,
        string currentPassword = Password
    ) =>
        account.Client.PostAsJsonAsync(
            "/api/users/me/email",
            new ChangeEmailRequest(newEmail, currentPassword)
        );

    private static string NewAddress() => $"changed-{Guid.NewGuid():N}@localhost.local";

    /// <summary>
    ///     This method turns the confirmation link in the message sent to
    ///     <paramref name="to" /> into the request the console page would submit.
    /// </summary>
    private static ConfirmEmailChangeRequest ReadConfirmation(
        RecordingEmailSender sender,
        string to
    )
    {
        string body = sender.Sent.Single(message => message.To == to).Body;
        const string marker = "href=\"";
        int start = body.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        string link = WebUtility.HtmlDecode(body[start..body.IndexOf('"', start)]);
        Assert.StartsWith($"{PublicBaseUrl}/confirm-email-change?", link);

        Dictionary<string, Microsoft.Extensions.Primitives.StringValues> query =
            QueryHelpers.ParseQuery(new Uri(link).Query);
        return new ConfirmEmailChangeRequest(
            Guid.Parse(query["userId"]),
            query["email"],
            query["token"]
        );
    }

    [Fact]
    public async Task ChangeMyEmail_WithConfirmationRequired_ChangesNothingUntilTheLinkIsOpened()
    {
        RecordingEmailSender sender = new();
        using WebApplicationFactory<Program> host = CreateHost(sender, true);
        Account account = await CreateAccountAsync(host);
        string newEmail = NewAddress();

        HttpResponseMessage response = await ChangeEmailAsync(account, newEmail);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.True(
            (await response.Content.ReadFromJsonAsync<ChangeEmailPendingResult>()).ConfirmationSent
        );
        Assert.Equal(account.Email, (await ReadAddressAsync(host, account.Id)).Email);
        Assert.Single(sender.Sent);
        Assert.Equal(newEmail, sender.Sent[0].To);

        // The session keeps working: nothing on the account has changed yet.
        Assert.Equal(
            HttpStatusCode.OK,
            (await account.Client.GetAsync("/api/users/me")).StatusCode
        );
    }

    [Fact]
    public async Task ConfirmEmailChange_WithTheCapturedLink_ChangesTheAddressNotifiesTheOldOneAndEndsSessions()
    {
        RecordingEmailSender sender = new();
        using WebApplicationFactory<Program> host = CreateHost(sender, true);
        Account account = await CreateAccountAsync(host);
        string newEmail = NewAddress();
        (await ChangeEmailAsync(account, newEmail)).EnsureSuccessStatusCode();
        ConfirmEmailChangeRequest confirmation = ReadConfirmation(sender, newEmail);

        HttpResponseMessage response = await host.CreateClient()
            .PostAsJsonAsync("/api/auth/confirm-email-change", confirmation);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        (string email, bool confirmed) = await ReadAddressAsync(host, account.Id);
        Assert.Equal(newEmail, email);
        Assert.True(confirmed);
        Assert.Contains(sender.Sent, message => message.To == account.Email);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await account.Client.GetAsync("/api/users/me")).StatusCode
        );

        // A link works once: the security stamp it was bound to is gone.
        HttpResponseMessage reused = await host.CreateClient()
            .PostAsJsonAsync("/api/auth/confirm-email-change", confirmation);
        Assert.Equal(HttpStatusCode.BadRequest, reused.StatusCode);
    }

    [Fact]
    public async Task ConfirmEmailChange_WithATamperedTokenOrAddressOrUnknownUser_ReturnsTheSameBadRequest()
    {
        RecordingEmailSender sender = new();
        using WebApplicationFactory<Program> host = CreateHost(sender, true);
        Account account = await CreateAccountAsync(host);
        string newEmail = NewAddress();
        (await ChangeEmailAsync(account, newEmail)).EnsureSuccessStatusCode();
        ConfirmEmailChangeRequest good = ReadConfirmation(sender, newEmail);

        char[] chars = good.Token.ToCharArray();
        chars[0] = chars[0] == 'A' ? 'B' : 'A';
        ConfirmEmailChangeRequest[] bad =
        [
            good with
            {
                Token = new string(chars),
            },
            good with
            {
                Token = "not base64url!",
            },
            good with
            {
                Email = NewAddress(),
            },
            good with
            {
                UserId = Guid.NewGuid(),
            },
        ];

        string firstBody = null;
        foreach (ConfirmEmailChangeRequest request in bad)
        {
            HttpResponseMessage response = await host.CreateClient()
                .PostAsJsonAsync("/api/auth/confirm-email-change", request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            // The same message for every failure, so the response does not say whether the
            // account exists.
            // The problem document also carries a per-request trace id, which is left out.
            using JsonDocument document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync()
            );
            string body = document.RootElement.GetProperty("detail").GetString();
            firstBody ??= body;
            Assert.Equal(firstBody, body);
        }

        Assert.Equal(account.Email, (await ReadAddressAsync(host, account.Id)).Email);
    }

    [Fact]
    public async Task ChangeMyEmail_WithConfirmationNotRequired_ChangesAtOnceAndEndsTheCallersSession()
    {
        RecordingEmailSender sender = new();
        using WebApplicationFactory<Program> host = CreateHost(sender, false);
        Account account = await CreateAccountAsync(host, emailConfirmed: true);
        string newEmail = NewAddress();

        HttpResponseMessage response = await ChangeEmailAsync(account, newEmail);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        UserProfileDetail profile = await response.Content.ReadFromJsonAsync<UserProfileDetail>();
        Assert.Equal(newEmail, profile.Email);
        Assert.False(profile.EmailChangeRequiresConfirmation);

        // The confirmed state stays as the administrator's change would leave it.
        (string email, bool confirmed) = await ReadAddressAsync(host, account.Id);
        Assert.Equal(newEmail, email);
        Assert.True(confirmed);

        Assert.Single(sender.Sent);
        Assert.Equal(account.Email, sender.Sent[0].To);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await account.Client.GetAsync("/api/users/me")).StatusCode
        );

        // Signing in again works and shows the new address.
        (HttpClient fresh, _) = await SignInAsync(host, account.UserName, Password);
        UserProfileDetail reread = await fresh.GetFromJsonAsync<UserProfileDetail>("/api/users/me");
        Assert.Equal(newEmail, reread.Email);
    }

    [Fact]
    public async Task ChangeMyEmail_WithNoSmtpConfigured_StillChangesAtOnceWhenConfirmationIsNotRequired()
    {
        RecordingEmailSender sender = new();
        using WebApplicationFactory<Program> host = CreateHost(sender, false, smtpHost: null);
        Account account = await CreateAccountAsync(host);

        HttpResponseMessage response = await ChangeEmailAsync(account, NewAddress());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task ChangeMyEmail_WithAWrongPassword_ReturnsBadRequestAndSendsNothing()
    {
        RecordingEmailSender sender = new();
        using WebApplicationFactory<Program> host = CreateHost(sender, true);
        Account account = await CreateAccountAsync(host);

        HttpResponseMessage response = await ChangeEmailAsync(
            account,
            NewAddress(),
            "Totally-Wrong-Password-0000!"
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "PasswordMismatch",
            await response.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Empty(sender.Sent);
        Assert.Equal(account.Email, (await ReadAddressAsync(host, account.Id)).Email);
    }

    [Fact]
    public async Task ChangeMyEmail_WithAnAddressAnotherAccountUses_ReturnsBadRequest()
    {
        RecordingEmailSender sender = new();
        using WebApplicationFactory<Program> host = CreateHost(sender, true);
        Account account = await CreateAccountAsync(host);
        Account other = await CreateAccountAsync(host);

        HttpResponseMessage response = await ChangeEmailAsync(account, other.Email);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "DuplicateEmail",
            await response.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task ChangeMyEmail_WithAnImmediateChangeToAUsedAddress_ReturnsBadRequestAndKeepsTheSession()
    {
        RecordingEmailSender sender = new();
        using WebApplicationFactory<Program> host = CreateHost(sender, false);
        Account account = await CreateAccountAsync(host);
        Account other = await CreateAccountAsync(host);

        HttpResponseMessage response = await ChangeEmailAsync(account, other.Email);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(account.Email, (await ReadAddressAsync(host, account.Id)).Email);
        Assert.Equal(
            HttpStatusCode.OK,
            (await account.Client.GetAsync("/api/users/me")).StatusCode
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-address")]
    public async Task ChangeMyEmail_WithAnInvalidAddress_ReturnsBadRequest(string newEmail)
    {
        RecordingEmailSender sender = new();
        using WebApplicationFactory<Program> host = CreateHost(sender, true);
        Account account = await CreateAccountAsync(host);

        HttpResponseMessage response = await ChangeEmailAsync(account, newEmail);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "InvalidEmail",
            await response.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task ChangeMyEmail_WithConfirmationRequiredAndNoSmtp_ReturnsBadRequestNamingSmtp()
    {
        RecordingEmailSender sender = new();
        using WebApplicationFactory<Program> host = CreateHost(sender, true, smtpHost: null);
        Account account = await CreateAccountAsync(host);

        HttpResponseMessage response = await ChangeEmailAsync(account, NewAddress());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("SMTP", await response.Content.ReadAsStringAsync());
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task ChangeMyEmail_WithConfirmationRequiredAndNoPublicBaseUrl_ReturnsBadRequestNamingTheBaseUrl()
    {
        RecordingEmailSender sender = new();
        using WebApplicationFactory<Program> host = CreateHost(sender, true, publicBaseUrl: null);
        Account account = await CreateAccountAsync(host);

        HttpResponseMessage response = await ChangeEmailAsync(account, NewAddress());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("public base URL", await response.Content.ReadAsStringAsync());
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task ChangeMyEmail_WithoutAToken_ReturnsUnauthorized()
    {
        HttpResponseMessage response = await factory
            .CreateClient()
            .PostAsJsonAsync("/api/users/me/email", new ChangeEmailRequest("a@b.example", "x"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMine_ReportsWhetherAnAddressChangeNeedsConfirmation()
    {
        RecordingEmailSender sender = new();
        using WebApplicationFactory<Program> host = CreateHost(sender, false);
        Account account = await CreateAccountAsync(host);

        UserProfileDetail profile = await account.Client.GetFromJsonAsync<UserProfileDetail>(
            "/api/users/me"
        );

        Assert.False(profile.EmailChangeRequiresConfirmation);
    }

    [Fact]
    public async Task PatchMine_WithAnEmailField_StillIgnoresIt()
    {
        RecordingEmailSender sender = new();
        using WebApplicationFactory<Program> host = CreateHost(sender, false);
        Account account = await CreateAccountAsync(host);

        HttpResponseMessage response = await account.Client.PatchAsJsonAsync(
            "/api/users/me",
            new { email = NewAddress(), displayName = "Renamed" }
        );

        response.EnsureSuccessStatusCode();
        Assert.Equal(account.Email, (await ReadAddressAsync(host, account.Id)).Email);
    }
}
