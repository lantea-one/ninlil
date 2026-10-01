using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Storage;

/// <summary>
///     This class supplies the Swagger example for <see cref="StorageEngineSwitchApiRequest" />,
///     the request body of <see cref="Controllers.StorageController.Switch" />: a switch to
///     PostgreSQL with only <see cref="StorageEngineSwitchApiRequest.PostgreSql" /> filled in,
///     matching <see cref="StorageEngineSwitchApiRequest.TargetEngine" />, and the Npgsql
///     <c>Require</c> SSL mode.
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
