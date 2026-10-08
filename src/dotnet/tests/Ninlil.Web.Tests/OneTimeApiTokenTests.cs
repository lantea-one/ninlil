using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Token;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers one-time API tokens end to end over HTTP on the shared in-process host
///     (SQLite). Each test creates a user with no permissions and a one-time token, then presents
///     it as <c>Authorization: ApiToken ...</c> to a permission-gated endpoint: 403 means the token
///     authenticated and only authorization failed, 401 means the token was rejected. The tests
///     check that an account rejected as disabled or locked out does not spend the token, and
///     that many simultaneous requests with one token produce exactly one success.
/// </summary>
[Collection(WebTestCollection.Name)]
public class OneTimeApiTokenTests(NinlilDnsWebApplicationFactory factory)
{
    private const string ProbeEndpoint = "/api/dns64-settings";

    private async Task<Guid> CreateUserAsync(string suffix)
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

        string userName = $"one-time-token-{suffix}";
        HttpResponseMessage created = await adminClient.PostAsJsonAsync(
            "/api/users",
            new CreateUserRequest(
                userName,
                "Original-Password-1234!",
                $"{userName}@localhost.local",
                "One-Time Token User"
            )
        );
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<UserSummary>()).Id;
    }

    private async Task<string> CreateOneTimeTokenAsync(Guid userId)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        IApiTokenService service = scope.ServiceProvider.GetRequiredService<IApiTokenService>();
        return (await service.CreateOneTimeTokenAsync(userId, "test", null)).RawToken;
    }

    private async Task UpdateUserAsync(Guid userId, Action<UserEntity> change)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        IUserRepository users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        UserEntity user = await users.GetByIdAsync(userId);
        change(user);
        users.Update(user);
        await unitOfWork.SaveChangesAsync();
    }

    private async Task<HttpStatusCode> PresentAsync(string rawToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, ProbeEndpoint);
        request.Headers.TryAddWithoutValidation("Authorization", $"ApiToken {rawToken}");
        HttpResponseMessage response = await factory.CreateClient().SendAsync(request);
        return response.StatusCode;
    }

    [Fact]
    public async Task ADisabledAccountIsRejectedWithoutSpendingTheTokenAndWorksOnceReEnabled()
    {
        Guid userId = await CreateUserAsync("disabled");
        string token = await CreateOneTimeTokenAsync(userId);

        await UpdateUserAsync(userId, user => user.IsDisabled = true);
        Assert.Equal(HttpStatusCode.Unauthorized, await PresentAsync(token));
        Assert.Equal(HttpStatusCode.Unauthorized, await PresentAsync(token));

        await UpdateUserAsync(userId, user => user.IsDisabled = false);
        Assert.Equal(HttpStatusCode.Forbidden, await PresentAsync(token));

        // Now it is spent.
        Assert.Equal(HttpStatusCode.Unauthorized, await PresentAsync(token));
    }

    [Fact]
    public async Task ALockedOutAccountIsRejectedWithoutSpendingTheToken()
    {
        Guid userId = await CreateUserAsync("locked");
        string token = await CreateOneTimeTokenAsync(userId);

        await UpdateUserAsync(
            userId,
            user =>
            {
                user.LockoutEnabled = true;
                user.LockoutEndUtc = DateTimeOffset.UtcNow.AddHours(1);
            }
        );
        Assert.Equal(HttpStatusCode.Unauthorized, await PresentAsync(token));

        await UpdateUserAsync(userId, user => user.LockoutEndUtc = null);
        Assert.Equal(HttpStatusCode.Forbidden, await PresentAsync(token));
    }

    [Fact]
    public async Task SimultaneousRequestsWithOneTokenProduceExactlyOneSuccess()
    {
        Guid userId = await CreateUserAsync("race");

        for (int round = 0; round < 5; round++)
        {
            string token = await CreateOneTimeTokenAsync(userId);

            using ManualResetEventSlim gate = new(false);
            Task<HttpStatusCode>[] attempts = Enumerable
                .Range(0, 16)
                .Select(_ =>
                    Task.Run(async () =>
                    {
                        gate.Wait();
                        return await PresentAsync(token);
                    })
                )
                .ToArray();
            gate.Set();
            HttpStatusCode[] results = await Task.WhenAll(attempts);

            Assert.Equal(1, results.Count(code => code == HttpStatusCode.Forbidden));
            Assert.Equal(15, results.Count(code => code == HttpStatusCode.Unauthorized));
        }
    }
}
