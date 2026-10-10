using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Storage;

/// <summary>
///     This class supplies the Swagger example for <see cref="StorageStatus" />, as returned by
///     <see cref="Controllers.StorageController.Status" />: an install on PostgreSQL with two
///     read replicas that currently serve reads, no switch waiting and none applied by this start.
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
            )
        );
}
