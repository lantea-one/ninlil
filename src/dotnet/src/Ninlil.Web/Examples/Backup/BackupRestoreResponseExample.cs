using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Backup;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="BackupRestoreResponse" />, referenced by
///     <see cref="Controllers.BackupController.Restore" />.
/// </summary>
public class BackupRestoreResponseExample : IExamplesProvider<BackupRestoreResponse>
{
    /// <summary>
    ///     This method returns a plausible-sized successful restore outcome, spanning a handful
    ///     of this server's own entity types, with the standard restart-required note every
    ///     successful restore actually returns verbatim.
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
