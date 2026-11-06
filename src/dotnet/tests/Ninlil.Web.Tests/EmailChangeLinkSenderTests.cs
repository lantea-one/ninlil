using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Identity.Email;
using Ninlil.Dns.Identity.Options;
using Ninlil.Web.Services;
using Ninlil.Web.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="EmailChangeLinkSender" />: the confirmation link is built from
///     <see cref="EmailSenderOptions.PublicBaseUrl" /> and never from the request's Host header, it
///     carries escaped values and a token that <see cref="UserManager{TUser}.ChangeEmailAsync" />
///     accepts, and the notice goes to the old address. The sender is built by hand from the shared
///     host's real <see cref="UserManager{TUser}" />, with fixed options and a fake
///     <see cref="IEmailSender" /> that records each message.
/// </summary>
[Collection(WebTestCollection.Name)]
public class EmailChangeLinkSenderTests(NinlilDnsWebApplicationFactory factory)
{
    private const string PublicBaseUrl = "https://dns.example.com/console";

    /// <summary>This class records every message it is asked to send.</summary>
    private sealed class CapturingEmailSender : IEmailSender
    {
        /// <summary>The messages sent so far, as (address, subject, body).</summary>
        public List<(string To, string Subject, string Body)> Sent { get; } = [];

        /// <inheritdoc />
        public Task SendAsync(
            string toAddress,
            string subject,
            string htmlBody,
            CancellationToken stoppingToken = default
        )
        {
            Sent.Add((toAddress, subject, htmlBody));
            return Task.CompletedTask;
        }
    }

    private static StaticOptionsMonitor<EmailSenderOptions> Options(string publicBaseUrl) =>
        new(
            new EmailSenderOptions
            {
                SmtpHost = "smtp.example.invalid",
                FromAddress = "notifications@example.invalid",
                PublicBaseUrl = publicBaseUrl,
            }
        );

    private static async Task<UserEntity> CreateUserAsync(UserManager<UserEntity> userManager)
    {
        string userName = $"email-change-user-{Guid.NewGuid():N}";
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
    public async Task CreateConfirmationLink_WithABaseUrl_ProducesALinkThatChangesTheAddress()
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        UserManager<UserEntity> userManager = scope.ServiceProvider.GetRequiredService<
            UserManager<UserEntity>
        >();
        UserEntity user = await CreateUserAsync(userManager);
        string newEmail = $"new+{Guid.NewGuid():N}@example.com";
        EmailChangeLinkSender sender = new(
            userManager,
            new CapturingEmailSender(),
            Options(PublicBaseUrl)
        );

        string link = await sender.CreateConfirmationLinkAsync(user, newEmail);

        Assert.StartsWith($"{PublicBaseUrl}/confirm-email-change?", link);
        Uri uri = new(link);
        Dictionary<string, Microsoft.Extensions.Primitives.StringValues> query =
            QueryHelpers.ParseQuery(uri.Query);
        Assert.Equal(user.Id.ToString(), query["userId"].ToString());
        Assert.Equal(newEmail, query["email"].ToString());
        Assert.Contains("email=" + Uri.EscapeDataString(newEmail), link);
        string token = Encoding.UTF8.GetString(
            WebEncoders.Base64UrlDecode(query["token"].ToString())
        );
        IdentityResult result = await userManager.ChangeEmailAsync(user, newEmail, token);
        Assert.True(result.Succeeded);
        Assert.Equal(newEmail, user.Email);
    }

    [Fact]
    public async Task CreateConfirmationLink_WithAForgedHostHeader_IsUnchanged()
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        UserManager<UserEntity> userManager = scope.ServiceProvider.GetRequiredService<
            UserManager<UserEntity>
        >();
        UserEntity user = await CreateUserAsync(userManager);
        string newEmail = $"forged-{Guid.NewGuid():N}@example.com";
        EmailChangeLinkSender sender = new(
            userManager,
            new CapturingEmailSender(),
            Options(PublicBaseUrl)
        );

        // The forged request is in scope while the link is built; the sender must not read it.
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext =
            new DefaultHttpContext
            {
                Request = { Scheme = "http", Host = new HostString("attacker.example") },
            };
        string link = await sender.CreateConfirmationLinkAsync(user, newEmail);

        Assert.StartsWith($"{PublicBaseUrl}/confirm-email-change?", link);
        Assert.DoesNotContain("attacker.example", link);
    }

    [Fact]
    public async Task CreateConfirmationLink_WithoutABaseUrl_ThrowsAValidationExceptionNamingTheSetting()
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        UserManager<UserEntity> userManager = scope.ServiceProvider.GetRequiredService<
            UserManager<UserEntity>
        >();
        UserEntity user = await CreateUserAsync(userManager);
        EmailChangeLinkSender sender = new(userManager, new CapturingEmailSender(), Options(null));

        ValidationException exception = await Assert.ThrowsAsync<ValidationException>(() =>
            sender.CreateConfirmationLinkAsync(user, "a@example.com")
        );
        Assert.Contains("publicBaseUrl", exception.Message);
        Assert.Equal(EmailChangeLinkSender.PublicBaseUrlNotConfiguredMessage, exception.Message);
    }

    [Fact]
    public async Task SendConfirmation_SendsTheLinkToTheNewAddress()
    {
        CapturingEmailSender emailSender = new();
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        EmailChangeLinkSender sender = new(
            scope.ServiceProvider.GetRequiredService<UserManager<UserEntity>>(),
            emailSender,
            Options(PublicBaseUrl)
        );

        await sender.SendConfirmationAsync(
            "new@example.com",
            "https://dns.example.com/console/confirm-email-change?a=1&b=<2>",
            CancellationToken.None
        );

        (string to, string subject, string body) = Assert.Single(emailSender.Sent);
        Assert.Equal("new@example.com", to);
        Assert.Equal("Confirm your new Ninlil e-mail address", subject);
        Assert.Contains(
            "href=\"https://dns.example.com/console/confirm-email-change?a=1&amp;b=&lt;2&gt;\"",
            body
        );
        Assert.Contains("ignore this e-mail", body);
    }

    [Fact]
    public async Task SendChangedNotice_GoesToTheOldAddressAndNamesTheNewOne()
    {
        CapturingEmailSender emailSender = new();
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        EmailChangeLinkSender sender = new(
            scope.ServiceProvider.GetRequiredService<UserManager<UserEntity>>(),
            emailSender,
            Options(PublicBaseUrl)
        );

        await sender.SendChangedNoticeAsync(
            "old@example.com",
            "new<x>@example.com",
            CancellationToken.None
        );

        (string to, string subject, string body) = Assert.Single(emailSender.Sent);
        Assert.Equal("old@example.com", to);
        Assert.Equal("Your Ninlil e-mail address was changed", subject);
        Assert.Contains("new&lt;x&gt;@example.com", body);
        Assert.Contains("administrator", body);
    }
}
