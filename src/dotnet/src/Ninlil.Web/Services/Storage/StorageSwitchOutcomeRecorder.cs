using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Services.Storage;

/// <summary>
///     This singleton holds the outcome of the storage switch that <see cref="PendingStorageSwitchApplier" />
///     ran during this process's startup. <c>GET /api/storage</c> reports it. It is empty when
///     this start applied no switch, and it is lost on the next restart.
/// </summary>
public sealed class StorageSwitchOutcomeRecorder
{
    /// <summary>
    ///     This property is the outcome of this start's switch, or null.
    /// </summary>
    public StorageSwitchOutcome Current { get; private set; }

    /// <summary>
    ///     This method records the outcome.
    /// </summary>
    public void Record(StorageSwitchOutcome outcome) => Current = outcome;
}
