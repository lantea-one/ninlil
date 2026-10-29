using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Repository;

/// <summary>
///     This class holds the tests of
///     <see cref="IDnsClientAddressRepository.ListByClientIdsAsync" />, the one-query address
///     lookup behind the admin client list, that run against every engine: one subclass per
///     engine supplies the fixture and a fresh database.
/// </summary>
public abstract class ClientAddressBatchTestsBase(EngineFixtureBase fixture)
{
    private static readonly DateTimeOffset Start = new(2026, 10, 29, 8, 0, 0, TimeSpan.Zero);

    private static DnsClientAddressEntity Address(Guid clientId, int number) =>
        new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = Start.AddSeconds(number),
            ClientId = clientId,
            ClientIdentifier = $"aa:bb:cc:dd:ee:{number:x2}",
            ProtocolVersion = InternetProtocolVersionEnum.IPv4,
            Address = $"192.0.2.{number}",
            FirstSeenUtc = Start,
            LastSeenUtc = Start.AddMinutes(number),
        };

    /// <summary>
    ///     The lookup returns exactly the addresses of the devices asked for: none of another
    ///     device's, none for a device without addresses or an unknown one, and the same rows in
    ///     the same order per device as the single-device lookup.
    /// </summary>
    [Fact]
    public async Task ListByClientIdsAsync_ReturnsExactlyTheAddressesOfTheGivenClients()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        Guid[] clientIds = [.. Enumerable.Range(0, 5).Select(_ => Guid.NewGuid())];
        int[] addressCounts = [0, 1, 3, 2, 4];

        await using (AsyncServiceScope scope = database.Services.CreateAsyncScope())
        {
            IDnsClientRepository clients =
                scope.ServiceProvider.GetRequiredService<IDnsClientRepository>();
            IDnsClientAddressRepository addresses =
                scope.ServiceProvider.GetRequiredService<IDnsClientAddressRepository>();

            int number = 1;
            for (int index = 0; index < clientIds.Length; index++)
            {
                await clients.AddAsync(
                    new DnsClientEntity
                    {
                        Id = clientIds[index],
                        CreatedAtUtc = Start.AddSeconds(index),
                        DisplayName = $"client-{index}",
                    }
                );
                for (int count = 0; count < addressCounts[index]; count++)
                    await addresses.AddAsync(Address(clientIds[index], number++));
            }

            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await using AsyncServiceScope check = database.Services.CreateAsyncScope();
        IDnsClientAddressRepository reader =
            check.ServiceProvider.GetRequiredService<IDnsClientAddressRepository>();

        // Three of the five clients, one without addresses, one unknown id, and one not asked
        // for (the fourth) whose rows must stay out.
        Guid[] asked = [clientIds[0], clientIds[1], clientIds[2], clientIds[4], Guid.NewGuid()];
        IReadOnlyList<DnsClientAddressEntity> result = await reader.ListByClientIdsAsync(asked);

        Assert.Equal(1 + 3 + 4, result.Count);
        Assert.All(result, address => Assert.Contains(address.ClientId, asked));
        Assert.DoesNotContain(result, address => address.ClientId == clientIds[3]);

        foreach (Guid clientId in asked)
        {
            IReadOnlyList<DnsClientAddressEntity> single = await reader.ListByClientIdAsync(
                clientId
            );
            Assert.Equal(
                single.Select(address => address.Id),
                result.Where(address => address.ClientId == clientId).Select(address => address.Id)
            );
        }
    }

    /// <summary>
    ///     An empty list of devices returns an empty list.
    /// </summary>
    [Fact]
    public async Task ListByClientIdsAsync_WithNoClientIds_ReturnsNothing()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();

        Assert.Empty(
            await scope
                .ServiceProvider.GetRequiredService<IDnsClientAddressRepository>()
                .ListByClientIdsAsync([])
        );
    }
}

/// <summary>This class runs <see cref="ClientAddressBatchTestsBase" /> on SQLite (no Docker).</summary>
[Collection(SqliteCollection.Name)]
public sealed class SqliteClientAddressBatchTests(SqliteEngineFixture fixture)
    : ClientAddressBatchTestsBase(fixture);

/// <summary>This class runs <see cref="ClientAddressBatchTestsBase" /> on PostgreSQL (Docker).</summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class PostgreSqlClientAddressBatchTests(PostgreSqlEngineFixture fixture)
    : ClientAddressBatchTestsBase(fixture);

/// <summary>This class runs <see cref="ClientAddressBatchTestsBase" /> on MySQL (Docker).</summary>
[Collection(MySqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MySqlClientAddressBatchTests(MySqlEngineFixture fixture)
    : ClientAddressBatchTestsBase(fixture);

/// <summary>This class runs <see cref="ClientAddressBatchTestsBase" /> on SQL Server (Docker).</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class SqlServerClientAddressBatchTests(SqlServerEngineFixture fixture)
    : ClientAddressBatchTestsBase(fixture);

/// <summary>
///     This class runs <see cref="ClientAddressBatchTestsBase" /> on a MongoDB replica set (Docker).
/// </summary>
[Collection(MongoDbCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbClientAddressBatchTests(MongoDbEngineFixture fixture)
    : ClientAddressBatchTestsBase(fixture);

/// <summary>
///     This class runs <see cref="ClientAddressBatchTestsBase" /> on a standalone MongoDB (Docker).
/// </summary>
[Collection(MongoDbStandaloneCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbStandaloneClientAddressBatchTests(MongoDbStandaloneEngineFixture fixture)
    : ClientAddressBatchTestsBase(fixture);
