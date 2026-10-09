using Microsoft.AspNetCore.Identity;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Web.Services;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="PasswordGenerator" /> without a host. For a spread of password
///     policies up to the caps the identity settings accept (length 128, 64 unique characters),
///     many generated passwords must pass ASP.NET Core Identity's own
///     <see cref="PasswordValidator{TUser}" /> for that policy, so the generator and the policy
///     cannot drift apart.
/// </summary>
public class PasswordGeneratorTests
{
    private const int PasswordsPerPolicy = 200;

    /// <summary>
    ///     This type is a user store that does nothing. <see cref="UserManager{TUser}" /> needs a
    ///     store to be constructed; the password validator only reads the manager's options.
    /// </summary>
    private sealed class EmptyUserStore : IUserStore<UserEntity>
    {
        public void Dispose() { }

        public Task<string> GetUserIdAsync(UserEntity user, CancellationToken stoppingToken) =>
            Task.FromResult(user.Id.ToString());

        public Task<string> GetUserNameAsync(UserEntity user, CancellationToken stoppingToken) =>
            Task.FromResult(user.UserName);

        public Task SetUserNameAsync(
            UserEntity user,
            string userName,
            CancellationToken stoppingToken
        ) => Task.CompletedTask;

        public Task<string> GetNormalizedUserNameAsync(
            UserEntity user,
            CancellationToken stoppingToken
        ) => Task.FromResult(user.UserName);

        public Task SetNormalizedUserNameAsync(
            UserEntity user,
            string normalizedName,
            CancellationToken stoppingToken
        ) => Task.CompletedTask;

        public Task<IdentityResult> CreateAsync(UserEntity user, CancellationToken stoppingToken) =>
            Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> UpdateAsync(UserEntity user, CancellationToken stoppingToken) =>
            Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> DeleteAsync(UserEntity user, CancellationToken stoppingToken) =>
            Task.FromResult(IdentityResult.Success);

        public Task<UserEntity> FindByIdAsync(string userId, CancellationToken stoppingToken) =>
            Task.FromResult<UserEntity>(null);

        public Task<UserEntity> FindByNameAsync(
            string normalizedUserName,
            CancellationToken stoppingToken
        ) => Task.FromResult<UserEntity>(null);
    }

    private static PasswordOptions Policy(int length, int unique, bool requireAll = true) =>
        new()
        {
            RequiredLength = length,
            RequiredUniqueChars = unique,
            RequireDigit = requireAll,
            RequireLowercase = requireAll,
            RequireUppercase = requireAll,
            RequireNonAlphanumeric = requireAll,
        };

    private static async Task AssertPoliciesAcceptedAsync(PasswordOptions policy)
    {
        IdentityOptions identityOptions = new() { Password = policy };
        UserManager<UserEntity> manager = new(
            new EmptyUserStore(),
            Microsoft.Extensions.Options.Options.Create(identityOptions),
            null,
            null,
            null,
            null,
            null,
            null,
            null
        );
        PasswordValidator<UserEntity> validator = new();
        UserEntity user = new() { Id = Guid.NewGuid(), UserName = "someone" };

        for (int i = 0; i < PasswordsPerPolicy; i++)
        {
            string password = PasswordGenerator.GenerateStrongRandomPassword(policy);

            IdentityResult result = await validator.ValidateAsync(manager, user, password);

            Assert.True(
                result.Succeeded,
                $"Policy {policy.RequiredLength}/{policy.RequiredUniqueChars} rejected '{password}': "
                    + string.Join(", ", result.Errors.Select(e => e.Code))
            );
            Assert.True(password.Length >= policy.RequiredLength);
            Assert.True(password.Distinct().Count() >= policy.RequiredUniqueChars);
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(12, 1)]
    [InlineData(12, 12)]
    [InlineData(24, 20)]
    [InlineData(24, 24)]
    [InlineData(25, 24)]
    [InlineData(40, 30)]
    [InlineData(64, 64)]
    [InlineData(100, 64)]
    [InlineData(128, 1)]
    [InlineData(128, 20)]
    [InlineData(128, 24)]
    [InlineData(128, 64)]
    public async Task GenerateStrongRandomPassword_ForAPolicyWithEveryClassRequired_PassesIdentityValidation(
        int length,
        int unique
    ) => await AssertPoliciesAcceptedAsync(Policy(length, unique));

    [Theory]
    [InlineData(6, 1)]
    [InlineData(30, 25)]
    [InlineData(128, 64)]
    public async Task GenerateStrongRandomPassword_ForAPolicyWithNoClassRequired_PassesIdentityValidation(
        int length,
        int unique
    ) => await AssertPoliciesAcceptedAsync(Policy(length, unique, requireAll: false));

    [Fact]
    public async Task GenerateStrongRandomPassword_WithoutAPolicy_PassesTheDefaultPolicy()
    {
        string password = PasswordGenerator.GenerateStrongRandomPassword();

        Assert.Equal(PasswordGenerator.MinimumGeneratedLength, password.Length);
        await AssertPoliciesAcceptedAsync(new PasswordOptions());
    }

    [Fact]
    public void GenerateStrongRandomPassword_TwiceInARow_ReturnsDifferentPasswords() =>
        Assert.NotEqual(
            PasswordGenerator.GenerateStrongRandomPassword(),
            PasswordGenerator.GenerateStrongRandomPassword()
        );
}
