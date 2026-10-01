using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Services.Storage;

/// <summary>
///     This interface switches the database to another relational engine inside the running
///     process: it migrates the target, backs up the current engine, points the live storage
///     settings at the target and restores the backup into it, without restarting.
///     <see cref="StorageEngineSwitchService" /> explains the mechanism. <c>StorageController</c>
///     uses it.
/// </summary>
public interface IStorageEngineSwitchService
{
    /// <summary>
    ///     This method reports the active storage engine, whether a switch is running and which
    ///     engines can be switched live.
    /// </summary>
    StorageStatus GetStatus();

    /// <summary>
    ///     This method switches the storage engine and returns once the process reads and writes
    ///     through the new engine with the data copied across. The switch is not saved to the
    ///     configuration file, so a restart returns to the previous engine.
    /// </summary>
    /// <param name="request">Which engine to switch to, and how to reach it.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <exception cref="Ninlil.Dns.Common.Exceptions.ValidationException">
    ///     Thrown when the request is malformed, names an engine that cannot be switched live
    ///     (<see cref="Ninlil.Dns.Common.Enum.StorageEngineEnum.MongoDb" />), already matches the
    ///     active storage, or the switch fails partway. After a failure the process uses the
    ///     previous engine, whose data is unchanged.
    /// </exception>
    /// <exception cref="Ninlil.Dns.Common.Exceptions.ConflictException">
    ///     Thrown when a switch is already in progress.
    /// </exception>
    Task<StorageEngineSwitchResult> SwitchEngineAsync(
        StorageEngineSwitchRequest request,
        CancellationToken stoppingToken = default
    );
}
