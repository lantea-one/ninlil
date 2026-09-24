// Define our namespace.
namespace Ninlil.Web.Services.Storage;

/// <summary>
///     This class tracks whether a storage-engine switch is currently in progress, shared as a
///     singleton between <see cref="IStorageEngineSwitchService" /> (which owns the transition)
///     and <see cref="Middleware.StorageSwitchGateMiddleware" /> (which reads it on every request
///     to reject ordinary traffic while a switch is underway). A switch briefly leaves this
///     process pointed at a freshly migrated, still-empty target database between the moment the
///     active <see cref="Ninlil.Dns.Data.Abstractions.Options.StorageOptions" /> singleton is
///     flipped and the moment the backup captured from the previous engine finishes being
///     restored into it -- an ordinary request answered from that narrow window would see a
///     database missing everything the operator expects, so every request other than the switch's
///     own status check is rejected for that window's short duration instead of risking a
///     confusing, silently-incomplete answer.
/// </summary>
public sealed class StorageEngineSwitchState
{
    private readonly Lock _gate = new();
    private bool _switchInProgress;

    /// <summary>
    ///     This property reports whether a switch is in progress right now.
    /// </summary>
    public bool IsSwitchInProgress
    {
        get
        {
            lock (_gate)
                return _switchInProgress;
        }
    }

    /// <summary>
    ///     This method atomically claims the "a switch is in progress" state, returning
    ///     <see langword="false" /> instead of throwing when one is already underway -- letting
    ///     the caller (<see cref="IStorageEngineSwitchService" />) translate a concurrent attempt
    ///     into its own REST-facing <c>ConflictException</c> rather than this class needing to
    ///     know anything about HTTP.
    /// </summary>
    /// <returns><see langword="true" /> if this call is the one that claimed the state.</returns>
    public bool TryBeginSwitch()
    {
        lock (_gate)
        {
            if (_switchInProgress)
                return false;

            _switchInProgress = true;
            return true;
        }
    }

    /// <summary>
    ///     This method releases the "a switch is in progress" state, always called from a
    ///     <c>finally</c> block by whichever call to <see cref="TryBeginSwitch" /> most recently
    ///     succeeded, regardless of whether the switch itself succeeded or failed.
    /// </summary>
    public void EndSwitch()
    {
        lock (_gate)
            _switchInProgress = false;
    }
}
