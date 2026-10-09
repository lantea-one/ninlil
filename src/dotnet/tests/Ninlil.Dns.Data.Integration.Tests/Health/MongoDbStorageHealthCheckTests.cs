using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;
using Ninlil.Web.HealthChecks;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Health;

/// <summary>
///     This class checks that <see cref="StorageReachabilityHealthCheck" /> reports Healthy for
///     a reachable MongoDB server (a real container). The unreachable case lives in
///     <c>Ninlil.Web.Tests</c>, which has no Docker. It needs Docker, so it carries the
///     <c>RequiresDocker</c> category.
/// </summary>
[Collection(MongoDbCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbStorageHealthCheckTests(MongoDbEngineFixture fixture)
{
    [Fact]
    public async Task CheckHealthAsync_OnReachableMongoDb_IsHealthyAndNamesMongoDb()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();

        StorageReachabilityHealthCheck check = new(
            database.Services.GetRequiredService<IServiceScopeFactory>(),
            database.Services.GetRequiredService<StorageOptions>()
        );

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("MongoDb", result.Description);
    }
}
