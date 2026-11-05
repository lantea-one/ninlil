using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Identity.Email;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <c>POST /api/auth/two-factor/email/send</c>
///     (<see cref="Controllers.TwoFactorController.SendEmailCode" />): a client that abandons the
///     request must stop the e-mail send. The test runs on a second host from
///     <see cref="NinlilDnsWebApplicationFactory.CreateSecondHost" /> whose
///     <see cref="IEmailSender" /> blocks until its token is cancelled, so the shared host keeps
///     its default sender for every other class.
/// </summary>
[Collection(WebTestCollection.Name)]
public class TwoFactorEmailCodeCancellationTests(NinlilDnsWebApplicationFactory factory)
{
    private const string Password = "Cancel-Email-Password-1234!";

    /// <summary>
    ///     This class is an <see cref="IEmailSender" /> that waits inside
    ///     <see cref="SendAsync" /> until the token it was given is cancelled, and records that
    ///     it saw the cancellation. A sender that was given no usable token never sees one.
    /// </summary>
    private sealed class BlockingEmailSender : IEmailSender
    {
        private readonly TaskCompletionSource _started = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource _cancelled = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        /// <summary>Completes when a send has started.</summary>
        public Task Started => _started.Task;

        /// <summary>Completes when a send saw its token cancelled.</summary>
        public Task Cancelled => _cancelled.Task;

        /// <summary>Lets a blocked send return, so a failed test leaves no request hanging.</summary>
        public void Release() => _release.TrySetResult();

        /// <inheritdoc />
        public async Task SendAsync(
            string toAddress,
            string subject,
            string htmlBody,
            CancellationToken stoppingToken = default
        )
        {
            _started.TrySetResult();
            using CancellationTokenRegistration registration = stoppingToken.Register(() =>
                _cancelled.TrySetResult()
            );
            await Task.WhenAny(_release.Task, _cancelled.Task);
            stoppingToken.ThrowIfCancellationRequested();
        }
    }

    [Fact]
    public async Task SendEmailCode_WhenTheClientCancels_CancelsTheEmailSend()
    {
        BlockingEmailSender sender = new();
        WebApplicationFactory<Program> host = factory.CreateSecondHost(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(sender);
            })
        );

        try
        {
            HttpClient admin = host.CreateClient();
            admin.DefaultRequestHeaders.Authorization = await SignInAsync(
                admin,
                NinlilDnsWebApplicationFactory.AdminUserName,
                NinlilDnsWebApplicationFactory.AdminPassword
            );
            string userName = $"cancel-email-{Guid.NewGuid():N}";
            HttpResponseMessage created = await admin.PostAsJsonAsync(
                "/api/users",
                new CreateUserRequest(
                    userName,
                    Password,
                    $"{userName}@localhost.local",
                    "Cancel Email"
                )
            );
            created.EnsureSuccessStatusCode();
            Guid id = (await created.Content.ReadFromJsonAsync<UserSummary>()).Id;

            await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
            {
                UserManager<UserEntity> userManager = scope.ServiceProvider.GetRequiredService<
                    UserManager<UserEntity>
                >();
                UserEntity user = await userManager.FindByIdAsync(id.ToString());
                user.EmailConfirmed = true;
                Assert.True((await userManager.UpdateAsync(user)).Succeeded);
            }

            HttpClient client = host.CreateClient();
            client.DefaultRequestHeaders.Authorization = await SignInAsync(
                client,
                userName,
                Password
            );

            using CancellationTokenSource cancelRequest = new();
            Task<HttpResponseMessage> request = client.PostAsync(
                "/api/auth/two-factor/email/send",
                null,
                cancelRequest.Token
            );

            await sender.Started.WaitAsync(TimeSpan.FromSeconds(30));
            await cancelRequest.CancelAsync();

            // The request token reaches the sender, so the abandoned send stops.
            Task finished = await Task.WhenAny(
                sender.Cancelled,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            Assert.Same(sender.Cancelled, finished);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        }
        finally
        {
            sender.Release();
            await host.DisposeAsync();
        }
    }

    private static async Task<AuthenticationHeaderValue> SignInAsync(
        HttpClient client,
        string userName,
        string password
    )
    {
        HttpResponseMessage login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(userName, password)
        );
        login.EnsureSuccessStatusCode();
        return new AuthenticationHeaderValue(
            "Bearer",
            (await login.Content.ReadFromJsonAsync<LoginResponse>()).AccessToken
        );
    }
}
