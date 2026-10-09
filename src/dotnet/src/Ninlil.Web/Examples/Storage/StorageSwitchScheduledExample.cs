using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Storage;

/// <summary>
///     This class supplies the Swagger example for <see cref="StorageSwitchScheduled" />, as
///     returned by <see cref="Controllers.StorageController.Switch" />: a switch to an empty
///     PostgreSQL database, with the storage environment variables deciding the settings, so the
///     operator must change them before the restart.
/// </summary>
public class StorageSwitchScheduledExample : IExamplesProvider<StorageSwitchScheduled>
{
    /// <inheritdoc />
    public StorageSwitchScheduled GetExamples() =>
        new(
            StorageEngineEnum.PostgreSql,
            DateTimeOffset.Parse("2026-09-01T12:00:00Z"),
            false,
            false,
            "Set NINLIL_STORAGE_ENGINE=PostgreSql and NINLIL_STORAGE_CONNECTION_STRING to the new "
                + "database (or change the environment variables), then restart. The data is "
                + "copied at that start."
        );
}
