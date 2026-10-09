using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Health;

/// <summary>
///     This class checks, against a real MySQL server, that
///     <c>UseNinlilDnsMySql</c> detects the server version once per connection string and not
///     once per DI scope. It reads the server's global <c>Connections</c> counter on a
///     non-pooled connection before and after a burst of scopes. It needs Docker, so it carries
///     the <c>RequiresDocker</c> category.
/// </summary>
[Collection(MySqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MySqlServerVersionDetectionTests(MySqlEngineFixture fixture)
{
    private static async Task<long> ReadConnectionsAsync(string connectionString)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = "SHOW GLOBAL STATUS LIKE 'Connections'";
        await using MySqlDataReader reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return long.Parse(reader.GetString(1));
    }

    [Fact]
    public async Task ResolvingContexts_In50Scopes_OpensAtMostTwoNewServerConnections()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        string measuring = fixture.BuildNonPooledConnectionString();

        long before = await ReadConnectionsAsync(measuring);

        for (int index = 0; index < 50; index++)
        {
            await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
            NinlilDnsContext context = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();

            // A real query on the pooled connection. CanConnectAsync is not used: Pomelo opens
            // its own non-pooled connection for it, which would count against the budget.
            await context.Database.ExecuteSqlRawAsync("SELECT 1");
        }

        long after = await ReadConnectionsAsync(measuring);

        // The second reading is itself a new connection. Before the version was cached, every
        // scope added one more (51 in total) for the detection alone.
        long opened = after - before - 1;
        Assert.True(opened <= 2, $"The 50 scopes opened {opened} new server connections.");
    }
}
