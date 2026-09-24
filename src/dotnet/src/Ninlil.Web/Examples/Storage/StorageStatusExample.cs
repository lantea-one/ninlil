using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Storage;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="StorageStatus" />, referenced by <see cref="Controllers.StorageController.Status" /> --
///     a fresh installation still running on its own auto-generated SQLite database, with no
///     switch currently in progress.
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
