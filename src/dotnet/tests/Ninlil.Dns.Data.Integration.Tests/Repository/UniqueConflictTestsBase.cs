using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Repository;

/// <summary>
///     This class holds the tests that run on every relational engine and show what
///     <see cref="IUnitOfWork.SaveChangesAsync" /> does with a write the database rejects. They use
///     the unique index on (scope, tag) of the DHCP custom options: the second insert of one tag
///     stands for the concurrent write that passes a controller's duplicate check, and it must
///     throw the <see cref="ConflictException" /> the API answers with 409. A foreign-key
///     violation (an option for a scope that does not exist) is a different failure and must stay
///     a <see cref="DbUpdateException" />. MongoDB is covered by
///     <c>MongoDhcpCustomOptionUniquenessTests</c>; it has no foreign keys.
/// </summary>
public abstract class UniqueConflictTestsBase(EngineFixtureBase fixture)
{
    private static async Task SaveScopeAsync(EngineDatabase database, Guid scopeId)
    {
        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<IDhcpScopeRepository>()
            .AddAsync(
                new DhcpScopeEntity
                {
                    Id = scopeId,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    Name = $"scope-{scopeId:N}",
                    ProtocolVersion = InternetProtocolVersionEnum.IPv4,
                    RangeStartAddress = "192.0.2.10",
                    RangeEndAddress = "192.0.2.20",
                    SubnetPrefixLength = 24,
                }
            );
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    private static async Task SaveOptionAsync(EngineDatabase database, Guid scopeId, byte tag)
    {
        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<IDhcpScopeCustomOptionRepository>()
            .AddAsync(
                new DhcpScopeCustomOptionEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ScopeId = scopeId,
                    OptionTag = tag,
                    ValueHex = "0A",
                }
            );
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    /// <summary>
    ///     A second option with the same tag on one scope throws the conflict, the original
    ///     driver error is logged at Warning, and another tag or scope is still accepted.
    /// </summary>
    [Fact]
    public async Task SaveChanges_WithASecondOptionOfTheSameTagOnAScope_ThrowsAConflictAndLogs()
    {
        CapturingLoggerProvider captured = new();
        await using EngineDatabase database = await fixture.CreateDatabaseAsync(
            configureServices: (services, _) => services.AddSingleton<ILoggerProvider>(captured)
        );
        Guid scopeId = Guid.NewGuid();
        Guid otherScopeId = Guid.NewGuid();
        await SaveScopeAsync(database, scopeId);
        await SaveScopeAsync(database, otherScopeId);
        await SaveOptionAsync(database, scopeId, 42);

        await Assert.ThrowsAsync<ConflictException>(() => SaveOptionAsync(database, scopeId, 42));

        Assert.Contains(
            captured.Entries,
            entry =>
                entry.Level == LogLevel.Warning
                && entry.Message.Contains("unique", StringComparison.OrdinalIgnoreCase)
        );
        await SaveOptionAsync(database, scopeId, 43);
        await SaveOptionAsync(database, otherScopeId, 42);
    }

    /// <summary>
    ///     An option for a scope that does not exist breaks the foreign key, not a unique index,
    ///     so the save keeps throwing the <see cref="DbUpdateException" />.
    /// </summary>
    [Fact]
    public async Task SaveChanges_WithAnOptionForAMissingScope_IsNotAConflict()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            SaveOptionAsync(database, Guid.NewGuid(), 42)
        );
    }
}

/// <summary>This class runs <see cref="UniqueConflictTestsBase" /> on SQLite (no Docker).</summary>
[Collection(SqliteCollection.Name)]
public sealed class SqliteUniqueConflictTests(SqliteEngineFixture fixture)
    : UniqueConflictTestsBase(fixture);

/// <summary>This class runs <see cref="UniqueConflictTestsBase" /> on PostgreSQL (Docker).</summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class PostgreSqlUniqueConflictTests(PostgreSqlEngineFixture fixture)
    : UniqueConflictTestsBase(fixture);

/// <summary>This class runs <see cref="UniqueConflictTestsBase" /> on MySQL (Docker).</summary>
[Collection(MySqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MySqlUniqueConflictTests(MySqlEngineFixture fixture)
    : UniqueConflictTestsBase(fixture);

/// <summary>This class runs <see cref="UniqueConflictTestsBase" /> on SQL Server (Docker).</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class SqlServerUniqueConflictTests(SqlServerEngineFixture fixture)
    : UniqueConflictTestsBase(fixture);
