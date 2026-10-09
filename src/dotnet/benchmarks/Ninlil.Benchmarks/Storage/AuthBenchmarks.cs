using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Benchmarks.Seeding;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Claims;

// Define our namespace.
namespace Ninlil.Benchmarks.Storage;

/// <summary>
///     This class measures the claim lookup behind every token-authenticated request:
///     <c>UserClaimsBuilder.BuildClaimsAsync</c> for a user with three roles (one claims query
///     per role today).
/// </summary>
[Config(typeof(StorageConfig))]
public class AuthBenchmarks : StorageBenchmarkBase
{
    private UserEntity _user;

    /// <inheritdoc />
    protected override async Task OnSetupAsync() =>
        _user = await InScopeAsync(provider =>
            provider
                .GetRequiredService<IUserRepository>()
                .FindByNormalizedUserNameAsync(BenchmarkDataSet.UserName(0).ToUpperInvariant())
        );

    /// <summary>Builds the claims of the user with three roles.</summary>
    [Benchmark]
    public Task<int> BuildClaims() =>
        InScopeAsync(async provider =>
            (
                await new UserClaimsBuilder(
                    provider.GetRequiredService<IUserRepository>(),
                    provider.GetRequiredService<IRoleRepository>()
                ).BuildClaimsAsync(_user)
            ).Count
        );
}
