using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Storage;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="StorageEngineSwitchResult" />, referenced by
///     <see cref="Controllers.StorageController.Switch" /> -- a completed switch from SQLite to
///     PostgreSQL, having carried a small, representative install's own data across.
/// </summary>
public class StorageEngineSwitchResultExample : IExamplesProvider<StorageEngineSwitchResult>
{
    /// <inheritdoc />
    public StorageEngineSwitchResult GetExamples()
    {
        DateTimeOffset startedAt = DateTimeOffset.Parse("2026-09-01T12:00:00Z");

        return new StorageEngineSwitchResult(
            StorageEngineEnum.Sqlite,
            StorageEngineEnum.PostgreSql,
            42,
            new Dictionary<string, int>
            {
                ["Zones"] = 3,
                ["Records"] = 27,
                ["Users"] = 2,
                ["Roles"] = 2,
            },
            startedAt,
            startedAt.AddSeconds(4)
        );
    }
}
