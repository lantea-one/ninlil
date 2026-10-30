using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Repository;

/// <summary>
///     This class holds the tests of <see cref="IDnsClientGroupNetworkRepository" /> that run
///     against every engine: one subclass per engine supplies the fixture and a fresh database.
///     They cover the row that matches by DNS-over-HTTPS ClientID and so has no CIDR, which the
///     schema must store as a null <c>Cidr</c>.
/// </summary>
public abstract class ClientGroupNetworkTestsBase(EngineFixtureBase fixture)
{
    private static readonly DateTimeOffset Start = new(2026, 10, 29, 8, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     A ClientID-only network is stored with a null CIDR, reads back with its ClientID, and
    ///     sits next to a CIDR-only network of the same group.
    /// </summary>
    [Fact]
    public async Task AddAsync_WithAClientIdAndNoCidr_StoresAndReadsTheRowBack()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        Guid groupId = Guid.NewGuid();
        Guid clientIdRowId = Guid.NewGuid();
        Guid cidrRowId = Guid.NewGuid();

        await using (AsyncServiceScope scope = database.Services.CreateAsyncScope())
        {
            await scope
                .ServiceProvider.GetRequiredService<IDnsClientGroupRepository>()
                .AddAsync(
                    new DnsClientGroupEntity
                    {
                        Id = groupId,
                        CreatedAtUtc = Start,
                        Name = $"group-{groupId:N}",
                    }
                );
            IDnsClientGroupNetworkRepository networks =
                scope.ServiceProvider.GetRequiredService<IDnsClientGroupNetworkRepository>();
            await networks.AddAsync(
                new DnsClientGroupNetworkEntity
                {
                    Id = clientIdRowId,
                    CreatedAtUtc = Start,
                    ClientGroupId = groupId,
                    ClientId = "living-room",
                }
            );
            await networks.AddAsync(
                new DnsClientGroupNetworkEntity
                {
                    Id = cidrRowId,
                    CreatedAtUtc = Start.AddSeconds(1),
                    ClientGroupId = groupId,
                    Cidr = "192.0.2.0/24",
                }
            );
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await using AsyncServiceScope check = database.Services.CreateAsyncScope();
        IDnsClientGroupNetworkRepository reader =
            check.ServiceProvider.GetRequiredService<IDnsClientGroupNetworkRepository>();

        DnsClientGroupNetworkEntity clientIdRow = await reader.GetByIdAsync(clientIdRowId);
        Assert.Null(clientIdRow.Cidr);
        Assert.Equal("living-room", clientIdRow.ClientId);

        DnsClientGroupNetworkEntity cidrRow = await reader.GetByIdAsync(cidrRowId);
        Assert.Equal("192.0.2.0/24", cidrRow.Cidr);
        Assert.Null(cidrRow.ClientId);

        Assert.Equal(2, (await reader.ListByGroupIdAsync(groupId)).Count);
    }
}

/// <summary>This class runs <see cref="ClientGroupNetworkTestsBase" /> on SQLite (no Docker).</summary>
[Collection(SqliteCollection.Name)]
public sealed class SqliteClientGroupNetworkTests(SqliteEngineFixture fixture)
    : ClientGroupNetworkTestsBase(fixture);

/// <summary>This class runs <see cref="ClientGroupNetworkTestsBase" /> on PostgreSQL (Docker).</summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class PostgreSqlClientGroupNetworkTests(PostgreSqlEngineFixture fixture)
    : ClientGroupNetworkTestsBase(fixture);

/// <summary>This class runs <see cref="ClientGroupNetworkTestsBase" /> on MySQL (Docker).</summary>
[Collection(MySqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MySqlClientGroupNetworkTests(MySqlEngineFixture fixture)
    : ClientGroupNetworkTestsBase(fixture);

/// <summary>This class runs <see cref="ClientGroupNetworkTestsBase" /> on SQL Server (Docker).</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class SqlServerClientGroupNetworkTests(SqlServerEngineFixture fixture)
    : ClientGroupNetworkTestsBase(fixture);

/// <summary>
///     This class runs <see cref="ClientGroupNetworkTestsBase" /> on a MongoDB replica set (Docker).
/// </summary>
[Collection(MongoDbCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbClientGroupNetworkTests(MongoDbEngineFixture fixture)
    : ClientGroupNetworkTestsBase(fixture);

/// <summary>
///     This class runs <see cref="ClientGroupNetworkTestsBase" /> on a standalone MongoDB (Docker).
/// </summary>
[Collection(MongoDbStandaloneCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbStandaloneClientGroupNetworkTests(MongoDbStandaloneEngineFixture fixture)
    : ClientGroupNetworkTestsBase(fixture);
