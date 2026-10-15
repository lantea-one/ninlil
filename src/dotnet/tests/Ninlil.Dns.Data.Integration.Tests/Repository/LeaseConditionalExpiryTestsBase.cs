using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Repository;

/// <summary>
///     This class holds the tests of <c>ILeaseRepository.TryExpireAsync</c>, the conditional write
///     the lease expiry sweep uses. It runs against every engine, one subclass per engine setup
///     supplying the fixture, because the relational engines run an <c>UPDATE ... WHERE</c> and
///     MongoDB an <c>updateOne</c> with the condition in its filter.
/// </summary>
public abstract class LeaseConditionalExpiryTestsBase(EngineFixtureBase fixture)
{
    /// <summary>An active lease past its expiry is expired and the call reports the change.</summary>
    [Fact]
    public async Task TryExpireAsync_OnAnActivePastExpiryLease_ExpiresIt()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        LeaseEntity lease = await SeedAsync(
            database,
            LeaseStateEnum.Active,
            TimeSpan.FromHours(-1)
        );
        DateTimeOffset now = DateTimeOffset.UtcNow;

        Assert.True(await TryExpireAsync(database, lease.Id, now));

        LeaseEntity reloaded = await LoadAsync(database, lease.Id);
        Assert.Equal(LeaseStateEnum.Expired, reloaded.State);
        Assert.Equal(lease.LeaseExpiresAtUtc, reloaded.LeaseExpiresAtUtc);
    }

    /// <summary>A lease renewed to a future expiry is left as it is and the call reports no change.</summary>
    [Fact]
    public async Task TryExpireAsync_OnARenewedLease_ChangesNothing()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        LeaseEntity lease = await SeedAsync(database, LeaseStateEnum.Active, TimeSpan.FromHours(1));

        Assert.False(await TryExpireAsync(database, lease.Id, DateTimeOffset.UtcNow));

        LeaseEntity reloaded = await LoadAsync(database, lease.Id);
        Assert.Equal(LeaseStateEnum.Active, reloaded.State);
        Assert.Equal(lease.LeaseExpiresAtUtc, reloaded.LeaseExpiresAtUtc);
    }

    /// <summary>A released lease is not touched, even when its expiry has passed.</summary>
    [Fact]
    public async Task TryExpireAsync_OnAReleasedLease_ChangesNothing()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        LeaseEntity lease = await SeedAsync(
            database,
            LeaseStateEnum.Released,
            TimeSpan.FromHours(-1)
        );

        Assert.False(await TryExpireAsync(database, lease.Id, DateTimeOffset.UtcNow));

        Assert.Equal(LeaseStateEnum.Released, (await LoadAsync(database, lease.Id)).State);
    }

    /// <summary>An identifier that matches no lease reports no change.</summary>
    [Fact]
    public async Task TryExpireAsync_OnAnUnknownId_ReturnsFalse()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();

        Assert.False(await TryExpireAsync(database, Guid.NewGuid(), DateTimeOffset.UtcNow));
    }

    private static async Task<bool> TryExpireAsync(
        EngineDatabase database,
        Guid leaseId,
        DateTimeOffset nowUtc
    )
    {
        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<ILeaseRepository>()
            .TryExpireAsync(leaseId, nowUtc);
    }

    private static async Task<LeaseEntity> LoadAsync(EngineDatabase database, Guid leaseId)
    {
        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<ILeaseRepository>()
            .GetByIdAsync(leaseId);
    }

    private static async Task<LeaseEntity> SeedAsync(
        EngineDatabase database,
        LeaseStateEnum state,
        TimeSpan expiresIn
    )
    {
        LeaseEntity lease = new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ProtocolVersion = InternetProtocolVersionEnum.IPv4,
            ScopeName = "expiry",
            ClientIdentifier = "client-expiry",
            IpAddress = "10.0.0.1",
            State = state,
            LeaseObtainedAtUtc = DateTimeOffset.UtcNow.AddHours(-2),
            LeaseExpiresAtUtc = DateTimeOffset.UtcNow.Add(expiresIn),
        };

        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ILeaseRepository>().AddAsync(lease);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

        // Re-read so the stored (tick-precision) instants are what the tests compare against.
        return await LoadAsync(database, lease.Id);
    }
}

/// <summary>This class runs <see cref="LeaseConditionalExpiryTestsBase" /> on SQLite (no Docker).</summary>
[Collection(SqliteCollection.Name)]
public sealed class SqliteLeaseConditionalExpiryTests(SqliteEngineFixture fixture)
    : LeaseConditionalExpiryTestsBase(fixture);

/// <summary>This class runs <see cref="LeaseConditionalExpiryTestsBase" /> on PostgreSQL (Docker).</summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class PostgreSqlLeaseConditionalExpiryTests(PostgreSqlEngineFixture fixture)
    : LeaseConditionalExpiryTestsBase(fixture);

/// <summary>This class runs <see cref="LeaseConditionalExpiryTestsBase" /> on MySQL (Docker).</summary>
[Collection(MySqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MySqlLeaseConditionalExpiryTests(MySqlEngineFixture fixture)
    : LeaseConditionalExpiryTestsBase(fixture);

/// <summary>This class runs <see cref="LeaseConditionalExpiryTestsBase" /> on SQL Server (Docker).</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class SqlServerLeaseConditionalExpiryTests(SqlServerEngineFixture fixture)
    : LeaseConditionalExpiryTestsBase(fixture);

/// <summary>This class runs <see cref="LeaseConditionalExpiryTestsBase" /> on a MongoDB replica set (Docker).</summary>
[Collection(MongoDbCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbLeaseConditionalExpiryTests(MongoDbEngineFixture fixture)
    : LeaseConditionalExpiryTestsBase(fixture);

/// <summary>This class runs <see cref="LeaseConditionalExpiryTestsBase" /> on a standalone MongoDB (Docker).</summary>
[Collection(MongoDbStandaloneCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbStandaloneLeaseConditionalExpiryTests(
    MongoDbStandaloneEngineFixture fixture
) : LeaseConditionalExpiryTestsBase(fixture);
