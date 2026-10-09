using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Storage;

/// <summary>
///     This class supplies the Swagger example for <see cref="StorageStatus" />, as returned by
///     <see cref="Controllers.StorageController.Status" />: an install on SQLite with a switch to
///     PostgreSQL waiting for the next start, and no switch applied by this start.
/// </summary>
public class StorageStatusExample : IExamplesProvider<StorageStatus>
{
    /// <inheritdoc />
    public StorageStatus GetExamples() =>
        new(
            StorageEngineEnum.Sqlite,
            new PendingStorageSwitchInfo(
                StorageEngineEnum.PostgreSql,
                DateTimeOffset.Parse("2026-09-01T12:00:00Z"),
                "admin",
                false
            ),
            null,
            [
                StorageEngineEnum.Sqlite,
                StorageEngineEnum.PostgreSql,
                StorageEngineEnum.MySql,
                StorageEngineEnum.SqlServer,
            ]
        );
}
