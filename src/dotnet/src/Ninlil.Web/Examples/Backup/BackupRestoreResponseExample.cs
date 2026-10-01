using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Backup;

/// <summary>
///     This class supplies the Swagger example for <see cref="BackupRestoreResponse" />, as
///     returned by <see cref="Controllers.BackupController.Restore" />.
/// </summary>
public class BackupRestoreResponseExample : IExamplesProvider<BackupRestoreResponse>
{
    /// <summary>
    ///     This method returns a small restore across a few entity types (the counts add up to the
    ///     total of 37) with the restart note every successful restore returns.
    /// </summary>
    public BackupRestoreResponse GetExamples() =>
        new(
            37,
            new Dictionary<string, int>
            {
                ["ZoneEntity"] = 3,
                ["RecordEntity"] = 24,
                ["TsigKeyEntity"] = 1,
                ["UserEntity"] = 2,
                ["RoleEntity"] = 1,
            },
            BackupRestoreResponse.RestartRequiredNote
        );
}
