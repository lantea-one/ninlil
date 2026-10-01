using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Storage;

/// <summary>
///     This class supplies the Swagger example for <see cref="StorageStatus" />, as returned by
///     <see cref="Controllers.StorageController.Status" />: a new install on its generated SQLite
///     database, with no switch running.
/// </summary>
public class StorageStatusExample : IExamplesProvider<StorageStatus>
{
    /// <inheritdoc />
    public StorageStatus GetExamples() =>
        new(
            StorageEngineEnum.Sqlite,
            false,
            [
                StorageEngineEnum.Sqlite,
                StorageEngineEnum.PostgreSql,
                StorageEngineEnum.MySql,
                StorageEngineEnum.SqlServer,
            ]
        );
}
