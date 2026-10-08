using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Common.Configuration;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Identity.Email;
using Ninlil.Dns.Identity.Extension;
using Ninlil.Dns.Identity.Options;
using Ninlil.Dns.Identity.Token;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Web.Controllers;
using Ninlil.Web.EnvironmentVariables;
using Ninlil.Web.Models;
using Ninlil.Web.Services;
using Ninlil.Web.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers how password-reset links are built and sent: from
///     <see cref="EmailSenderOptions.PublicBaseUrl" /> and never from the request's Host header, not
///     at all without that setting, and, for <see cref="AuthController.ForgotPassword" />, without
///     the response waiting on SMTP or going unlimited.
///     <para>
///         The shared host has no SMTP server and its settings file is not a configuration source,
///         so these tests build <see cref="PasswordResetLinkSender" /> and
///         <see cref="AuthController" /> by hand from the host's real services (the account
///         store, token providers and so on), with fixed options and a fake
///         <see cref="IEmailSender" /> that records each message and can hold it back.
///     </para>
/// </summary>
[Collection(WebTestCollection.Name)]
public class PasswordResetLinkTests(NinlilDnsWebApplicationFactory factory)
{
    private const string PublicBaseUrl = "https://dns.example.com/console";

    /// <summary>
    ///     This class records every message it is asked to send. Each send waits for
    ///     <see cref="Release" />, so a test can tell whether a caller waits for delivery.
    /// </summary>
    private sealed class RecordingEmailSender : IEmailSender
    {
        private readonly TaskCompletionSource _gate = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public TaskCompletionSource<string> FirstBody { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool HoldDelivery { get; init; }

        public void Release() => _gate.TrySetResult();

        public async Task SendAsync(
            string toAddress,
            string subject,
            string htmlBody,
            CancellationToken stoppingToken = default
        )
        {
            FirstBody.TrySetResult(htmlBody);

            if (HoldDelivery)
                await _gate.Task;
        }
    }

    private static EmailSenderOptions ConfiguredOptions(string publicBaseUrl) =>
        new()
        {
            SmtpHost = "smtp.example.invalid",
            FromAddress = "notifications@example.invalid",
            PublicBaseUrl = publicBaseUrl,
        };

    private static async Task<UserEntity> CreateUserAsync(UserManager<UserEntity> userManager)
    {
        string userName = $"reset-link-user-{Guid.NewGuid():N}";
        UserEntity user = new()
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            Email = $"{userName}@localhost.local",
        };

        IdentityResult result = await userManager.CreateAsync(user, "Original-Password-1234!");
        Assert.True(result.Succeeded);

        return user;
    }

    [Fact]
    public async Task TheResetLinkPointsAtTheConfiguredPublicBaseUrl()
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        UserManager<UserEntity> userManager = scope.ServiceProvider.GetRequiredService<
            UserManager<UserEntity>
        >();
        UserEntity user = await CreateUserAsync(userManager);
        RecordingEmailSender emailSender = new();

        PasswordResetLinkSender sender = new(
            userManager,
            emailSender,
            new StaticOptionsMonitor<EmailSenderOptions>(ConfiguredOptions(PublicBaseUrl))
        );
        await sender.SendAsync(user, CancellationToken.None);

        string body = await emailSender.FirstBody.Task;
        Assert.Contains($"href=\"{PublicBaseUrl}/reset-password?userId={user.Id}&token=", body);
    }

    [Fact]
    public async Task WithNoPublicBaseUrlNoLinkIsSentAndTheAdministratorIsTold()
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        UserManager<UserEntity> userManager = scope.ServiceProvider.GetRequiredService<
            UserManager<UserEntity>
        >();
        UserEntity user = await CreateUserAsync(userManager);
        RecordingEmailSender emailSender = new();

        PasswordResetLinkSender sender = new(
            userManager,
            emailSender,
            new StaticOptionsMonitor<EmailSenderOptions>(ConfiguredOptions(null))
        );

        ValidationException exception = await Assert.ThrowsAsync<ValidationException>(() =>
            sender.SendAsync(user, CancellationToken.None)
        );
        Assert.Equal(PasswordResetLinkSender.PublicBaseUrlNotConfiguredMessage, exception.Message);
        Assert.False(emailSender.FirstBody.Task.IsCompleted);
    }

    [Theory]
    [InlineData("dns.example.com")]
    [InlineData("ftp://dns.example.com")]
    [InlineData("https://dns.example.com/?next=evil")]
    public void APublicBaseUrlThatIsNotAPlainHttpUrlIsRejected(string value) =>
        Assert.False(PasswordResetLinkSender.TryParsePublicBaseUrl(value, out _));

    /// <summary>
    ///     This test sends a Forgot Password request with a forged Host header, as an attacker
    ///     would to poison the link, while the fake sender holds delivery back. The action must
    ///     answer before delivery finishes (so SMTP timing reveals nothing) and the link must still
    ///     use the configured address.
    /// </summary>
    [Fact]
    public async Task ForgotPasswordIgnoresAForgedHostAndDoesNotWaitForDelivery()
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        IServiceProvider services = scope.ServiceProvider;
        UserManager<UserEntity> userManager = services.GetRequiredService<
            UserManager<UserEntity>
        >();
        UserEntity user = await CreateUserAsync(userManager);

        RecordingEmailSender emailSender = new() { HoldDelivery = true };
        ServiceCollection deliveryServices = new();
        deliveryServices.AddSingleton<IEmailSender>(emailSender);
        await using ServiceProvider deliveryProvider = deliveryServices.BuildServiceProvider();

        StaticOptionsMonitor<EmailSenderOptions> emailOptions = new(
            ConfiguredOptions(PublicBaseUrl)
        );
        AuthController controller = new(
            userManager,
            services.GetRequiredService<SignInManager<UserEntity>>(),
            services.GetRequiredService<IJwtTokenService>(),
            services.GetRequiredService<PendingTwoFactorSignInCache>(),
            new PasswordResetLinkSender(userManager, emailSender, emailOptions),
            emailSender,
            emailOptions,
            services.GetRequiredService<IDnsServerEventPublisher>(),
            services.GetRequiredService<IOptionsMonitor<ClusterOptions>>(),
            deliveryProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AuthController>.Instance
        )
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    Request = { Scheme = "http", Host = new HostString("attacker.example") },
                },
            },
        };

        Task<ActionResult<ForgotPasswordResult>> request = controller.ForgotPassword(
            new ForgotPasswordRequest(user.Email)
        );
        Task finished = await Task.WhenAny(request, Task.Delay(TimeSpan.FromSeconds(10)));

        try
        {
            Assert.Same(request, finished);
            Assert.IsType<OkObjectResult>((await request).Result);

            string body = await emailSender.FirstBody.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Contains($"href=\"{PublicBaseUrl}/reset-password?", body);
            Assert.DoesNotContain("attacker.example", body);
        }
        finally
        {
            emailSender.Release();
        }
    }

    [Fact]
    public void ForgotPasswordIsRateLimitedByItsOwnPolicy()
    {
        EnableRateLimitingAttribute attribute = typeof(AuthController)
            .GetMethod(nameof(AuthController.ForgotPassword))
            .GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: false)
            .Cast<EnableRateLimitingAttribute>()
            .Single();

        Assert.Equal(
            ServiceCollectionAdminLoginRateLimitingExtension.PasswordResetEndpointPolicyName,
            attribute.PolicyName
        );
    }

    [Theory]
    [InlineData("NINLIL_EMAIL_PUBLIC_BASE_URL", "Email:PublicBaseUrl")]
    [InlineData("NINLIL_ALLOWED_HOSTS", "AllowedHosts")]
    public void TheNewSettingsHaveTheirDocumentedEnvironmentVariables(
        string variableName,
        string configurationPath
    )
    {
        IReadOnlyDictionary<string, string> mappings = OptionsEnvironmentVariableMap.BuildMappings(
            OptionsEnvironmentVariableRegistry.Roots,
            OptionsEnvironmentVariableRegistry.StandaloneConfigurationPaths
        );

        Assert.True(mappings.TryGetValue(variableName, out string path), variableName);
        Assert.Equal(configurationPath, path);
    }
}
