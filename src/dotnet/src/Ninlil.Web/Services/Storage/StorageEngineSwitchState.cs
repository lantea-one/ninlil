// Define our namespace.
namespace Ninlil.Web.Services.Storage;

/// <summary>
///     This class tracks whether a storage engine switch is running. It is a singleton shared by
///     <see cref="IStorageEngineSwitchService" />, which sets it, and
///     <see cref="Middleware.StorageSwitchGateMiddleware" />, which checks it on every request.
///     Between the flip of the <see cref="Ninlil.Dns.Data.Abstractions.Options.StorageOptions" />
///     singleton and the end of the restore, the process points at a migrated but still empty
///     database, so while a switch runs the middleware answers every <c>/api</c> request except
///     <c>GET /api/storage</c> with 503 rather than give an incomplete answer. Requests outside
///     <c>/api</c> and background services are not blocked.
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
    ///     This method claims the in-progress flag atomically, returning <see langword="false" />
    ///     when a switch is already running, so the caller can turn that into a
    ///     <c>ConflictException</c>.
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
    ///     This method clears the in-progress flag. The caller that claimed it calls this from a
    ///     <c>finally</c> block, whether or not the switch succeeded.
    /// </summary>
    public void EndSwitch()
    {
        lock (_gate)
            _switchInProgress = false;
    }
}
