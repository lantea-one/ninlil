using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Storage;

/// <summary>
///     This class supplies the Swagger example for <see cref="StorageEngineSwitchResult" />, as
///     returned by <see cref="Controllers.StorageController.Switch" />: a switch from SQLite to
///     PostgreSQL that copied a small install's data. The listed counts sum to 34 rather than the
///     reported 42; a real response lists every entity type.
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
