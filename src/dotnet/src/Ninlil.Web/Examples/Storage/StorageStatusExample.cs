using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Storage;

/// <summary>
///     This class supplies the Swagger example for <see cref="StorageStatus" />, as returned by
///     <see cref="Controllers.StorageController.Status" />: an install on PostgreSQL with two
///     read replicas that currently serve reads, no switch waiting and none applied by this start.
///     The topology variable list is shortened to six names.
/// </summary>
public class StorageStatusExample : IExamplesProvider<StorageStatus>
{
    /// <inheritdoc />
    public StorageStatus GetExamples() =>
        new(
            StorageEngineEnum.PostgreSql,
            null,
            null,
            [
                StorageEngineEnum.Sqlite,
                StorageEngineEnum.PostgreSql,
                StorageEngineEnum.MySql,
                StorageEngineEnum.SqlServer,
            ],
            new StoragePrimaryStatus(["db-primary.example.net:5432"]),
            new StorageReadReplicaStatus(
                true,
                true,
                0.4,
                true,
                DateTimeOffset.Parse("2026-09-01T12:00:03Z"),
                "",
                18452,
                3107,
                ["db-standby-1.example.net:5432", "db-standby-2.example.net:5432"],
                null
            ),
            [
                "NINLIL_STORAGE_READ_REPLICA_CONNECTION_STRING",
                "NINLIL_STORAGE_READ_REPLICA_STICKY_AFTER_WRITE_SECONDS",
                "NINLIL_STORAGE_READ_REPLICA_MAX_LAG_SECONDS",
                "NINLIL_STORAGE_READ_REPLICA_LAG_CHECK_INTERVAL_SECONDS",
                "NINLIL_STORAGE_POSTGRESQL_TARGET_SESSION_ATTRIBUTES",
                "NINLIL_STORAGE_MONGODB_REPLICA_SET",
            ]
        );
}
