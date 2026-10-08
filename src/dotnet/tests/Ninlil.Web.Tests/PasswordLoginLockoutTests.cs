using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers how password sign-in treats Identity lockout, over HTTP on the shared
///     in-process host (which has the per-IP login limiter off). Wrong passwords must not lock
///     an account, because anyone who knows a username could otherwise lock it out; an account
///     that an administrator or the system has already locked out must still be refused with 423,
///     even with the correct password.
/// </summary>
[Collection(WebTestCollection.Name)]
public class PasswordLoginLockoutTests(NinlilDnsWebApplicationFactory factory)
{
    private const string Password = "Lockout-Test-Password-1234!";

    private async Task<(string UserName, Guid UserId)> CreateUserAsync()
    {
        HttpClient adminClient = factory.CreateClient();
        HttpResponseMessage login = await adminClient.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(
                NinlilDnsWebApplicationFactory.AdminUserName,
                NinlilDnsWebApplicationFactory.AdminPassword
            )
        );
        login.EnsureSuccessStatusCode();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            (await login.Content.ReadFromJsonAsync<LoginResponse>()).AccessToken
        );

        string userName = $"lockout-{Guid.NewGuid():N}";
        HttpResponseMessage created = await adminClient.PostAsJsonAsync(
            "/api/users",
            new CreateUserRequest(userName, Password, $"{userName}@localhost.local", "Lockout")
        );
        created.EnsureSuccessStatusCode();
        return (userName, (await created.Content.ReadFromJsonAsync<UserSummary>()).Id);
    }

    private Task<HttpResponseMessage> LoginAsync(string userName, string password) =>
        factory
            .CreateClient()
            .PostAsJsonAsync("/api/auth/login", new LoginRequest(userName, password));

    [Fact]
    public async Task ManyWrongPasswordsDoNotLockTheAccount()
    {
        (string userName, _) = await CreateUserAsync();

        for (int i = 0; i < 8; i++)
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await LoginAsync(userName, "not-the-password")).StatusCode
            );

        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(userName, Password)).StatusCode);
    }

    [Fact]
    public async Task AnAccountLockedOutByAnAdministratorIsStillRefusedWithTheCorrectPassword()
    {
        (string userName, Guid userId) = await CreateUserAsync();
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            IUserRepository users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            UserEntity user = await users.GetByIdAsync(userId);
            user.LockoutEnabled = true;
            user.LockoutEndUtc = DateTimeOffset.UtcNow.AddHours(1);
            users.Update(user);
            await unitOfWork.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Locked, (await LoginAsync(userName, Password)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Locked,
            (await LoginAsync(userName, "not-the-password")).StatusCode
        );
    }
}
