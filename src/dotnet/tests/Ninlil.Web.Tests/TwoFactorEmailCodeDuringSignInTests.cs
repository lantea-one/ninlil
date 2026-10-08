using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Identity.Email;
using Ninlil.Dns.Identity.Extension;
using Ninlil.Dns.Identity.Options;
using Ninlil.Dns.Identity.Token;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;
using Ninlil.Web.Services;
using Ninlil.Web.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="AuthController.SendTwoFactorEmailCode" />, which requests the
///     e-mailed second-factor code while a sign-in is waiting for its second factor and the caller
///     has only the pending-sign-in token. The HTTP tests run on the shared host (which has no
///     SMTP server, so they stop before delivery); the delivery test builds the controller by hand
///     with a recording <see cref="IEmailSender" />, as <see cref="PasswordResetLinkTests" /> does.
/// </summary>
[Collection(WebTestCollection.Name)]
public class TwoFactorEmailCodeDuringSignInTests(NinlilDnsWebApplicationFactory factory)
{
    private const string EmailCodeUrl = "/api/auth/login/two-factor/email-code";

    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<(string To, string Body)> Sent { get; } = [];

        public Task SendAsync(
            string toAddress,
            string subject,
            string htmlBody,
            CancellationToken stoppingToken = default
        )
        {
            Sent.Add((toAddress, htmlBody));
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     This method creates a user enrolled in TOTP and signs in with the password only,
    ///     returning the pending-sign-in token.
    /// </summary>
    private async Task<string> StartPendingSignInAsync()
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

        string userName = $"pending-email-{Guid.NewGuid():N}";
        const string password = "Pending-Email-Password-1234!";
        (
            await adminClient.PostAsJsonAsync(
                "/api/users",
                new CreateUserRequest(
                    userName,
                    password,
                    $"{userName}@localhost.local",
                    "Pending Email"
                )
            )
        ).EnsureSuccessStatusCode();

        HttpClient userClient = factory.CreateClient();
        HttpResponseMessage userLogin = await userClient.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(userName, password)
        );
        userLogin.EnsureSuccessStatusCode();
        userClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            (await userLogin.Content.ReadFromJsonAsync<LoginResponse>()).AccessToken
        );

        HttpResponseMessage setup = await userClient.PostAsync(
            "/api/auth/two-factor/authenticator/setup",
            null
        );
        setup.EnsureSuccessStatusCode();
        string secret = (
            await setup.Content.ReadFromJsonAsync<AuthenticatorSetupResult>()
        ).SharedKey;
        (
            await userClient.PostAsJsonAsync(
                "/api/auth/two-factor/authenticator/enable",
                new TwoFactorCodeRequest(TotpGenerator.GenerateCode(secret))
            )
        ).EnsureSuccessStatusCode();

        HttpResponseMessage pending = await factory
            .CreateClient()
            .PostAsJsonAsync("/api/auth/login", new LoginRequest(userName, password));
        pending.EnsureSuccessStatusCode();
        LoginResponse challenge = await pending.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.True(challenge.RequiresTwoFactor);

        return challenge.MfaToken;
    }

    [Fact]
    public async Task APendingTokenAloneIsEnoughToReachTheAction()
    {
        string mfaToken = await StartPendingSignInAsync();

        // No Authorization header. The account's e-mail is unconfirmed, so the action answers 400
        // for that reason; a 401 would mean the pending token was not accepted.
        HttpResponseMessage response = await factory
            .CreateClient()
            .PostAsJsonAsync(EmailCodeUrl, new TwoFactorEmailCodeRequest(mfaToken));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AnUnknownTokenIsRejected()
    {
        HttpResponseMessage response = await factory
            .CreateClient()
            .PostAsJsonAsync(EmailCodeUrl, new TwoFactorEmailCodeRequest("not-a-pending-token"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RequestingTheCodeLeavesThePendingTokenUsable()
    {
        string mfaToken = await StartPendingSignInAsync();
        HttpClient client = factory.CreateClient();

        (
            await client.PostAsJsonAsync(EmailCodeUrl, new TwoFactorEmailCodeRequest(mfaToken))
        ).Dispose();

        // A second request still reaches the e-mail check (400, not 401), so the first one did
        // not consume the token.
        HttpResponseMessage second = await client.PostAsJsonAsync(
            EmailCodeUrl,
            new TwoFactorEmailCodeRequest(mfaToken)
        );
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task TheCodeIsEmailedToAnAccountWithAConfirmedAddress()
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        IServiceProvider services = scope.ServiceProvider;
        UserManager<UserEntity> userManager = services.GetRequiredService<
            UserManager<UserEntity>
        >();
        string userName = $"pending-email-direct-{Guid.NewGuid():N}";
        UserEntity user = new()
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            Email = $"{userName}@localhost.local",
            EmailConfirmed = true,
        };
        Assert.True((await userManager.CreateAsync(user, "Direct-Password-1234!")).Succeeded);

        PendingTwoFactorSignInCache cache =
            services.GetRequiredService<PendingTwoFactorSignInCache>();
        string mfaToken = cache.Create(user.Id);
        RecordingEmailSender emailSender = new();
        StaticOptionsMonitor<EmailSenderOptions> emailOptions = new(new EmailSenderOptions());
        AuthController controller = new(
            userManager,
            services.GetRequiredService<SignInManager<UserEntity>>(),
            services.GetRequiredService<IJwtTokenService>(),
            cache,
            new PasswordResetLinkSender(userManager, emailSender, emailOptions),
            emailSender,
            emailOptions,
            services.GetRequiredService<IDnsServerEventPublisher>(),
            services.GetRequiredService<IOptionsMonitor<ClusterOptions>>(),
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AuthController>.Instance
        )
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        IActionResult result = await controller.SendTwoFactorEmailCode(
            new TwoFactorEmailCodeRequest(mfaToken),
            CancellationToken.None
        );

        Assert.IsType<AcceptedResult>(result);
        (string to, string body) = Assert.Single(emailSender.Sent);
        Assert.Equal(user.Email, to);
        string code = await userManager.GenerateTwoFactorTokenAsync(
            user,
            TokenOptions.DefaultEmailProvider
        );
        Assert.Contains(code, body);

        // Still pending, so the code can be submitted next.
        Assert.Equal(user.Id, cache.Consume(mfaToken));
    }

    [Fact]
    public void TheActionUsesTheAuthenticationRateLimitPolicy()
    {
        EnableRateLimitingAttribute attribute = typeof(AuthController)
            .GetMethod(nameof(AuthController.SendTwoFactorEmailCode))
            .GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: false)
            .Cast<EnableRateLimitingAttribute>()
            .Single();

        Assert.Equal(
            ServiceCollectionAdminLoginRateLimitingExtension.AuthenticationEndpointPolicyName,
            attribute.PolicyName
        );
    }
}
