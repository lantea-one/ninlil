using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Storage;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="StorageEngineSwitchApiRequest" />, referenced by
///     <see cref="Controllers.StorageController.Switch" /> -- a switch onto a PostgreSQL server,
///     with only the <see cref="StorageEngineSwitchApiRequest.PostgreSql" /> section populated,
///     matching <see cref="StorageEngineSwitchApiRequest.TargetEngine" />.
/// </summary>
public class StorageEngineSwitchApiRequestExample : IExamplesProvider<StorageEngineSwitchApiRequest>
{
    /// <inheritdoc />
    public StorageEngineSwitchApiRequest GetExamples() =>
        new(
            StorageEngineEnum.PostgreSql,
            Sqlite: null,
            PostgreSql: new RelationalStorageConnectionFields(
                "db.example.internal",
                5432,
                "ninlil",
                "ninlil_app",
                "a-real-strong-password",
                "Require"
            ),
            MySql: null,
            SqlServer: null
        );
}
