using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Exceptions;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.EntityFrameworkCore.Topology;

// Define our namespace.
namespace Ninlil.Dns.Data.Tests;

/// <summary>
///     These tests cover the MySQL load-balance settings that
///     <see cref="StorageConnectionStringComposer" /> writes. They compose connection strings in
///     memory and read the result back; no driver connects. The passwords in the strings are
///     fake, and the refusal messages must not repeat them.
/// </summary>
public sealed class MySqlConnectionStringComposerTests
{
    private const string MySqlSingle = "Server=primary;Database=ninlil;User=u;Password=hunter2";
    private const string MySqlReplicas = "Server=r1,r2;Database=ninlil;User=u;Password=hunter2";

    private static StorageOptions Options(string replica)
    {
        StorageOptions options = new()
        {
            Engine = StorageEngineEnum.MySql,
            ConnectionString = MySqlSingle,
        };
        options.ReadReplica.ConnectionString = replica;
        return options;
    }

    private static DatabaseStartupRefusedException Refused(StorageOptions options)
    {
        DatabaseStartupRefusedException refusal = Assert.Throws<DatabaseStartupRefusedException>(
            () =>
                StorageConnectionStringComposer.Compose(options)
        );
        Assert.DoesNotContain("hunter2", refusal.Message);
        return refusal;
    }

    [Fact]
    public void Compose_WithACommaListLoadBalance_IsRefused()
    {
        foreach (string mode in new[] { "1,2", "Random,LeastConnections" })
        {
            StorageOptions options = Options(MySqlReplicas);
            options.MySql.ReplicaLoadBalance = mode;

            Assert.Contains("ReplicaLoadBalance", Refused(options).Message);
        }

        StorageOptions accepted = Options(MySqlReplicas);
        accepted.MySql.ReplicaLoadBalance = "random";

        Assert.Contains(
            "Load Balance=Random",
            StorageConnectionStringComposer.Compose(accepted).Replica
        );
    }
}
