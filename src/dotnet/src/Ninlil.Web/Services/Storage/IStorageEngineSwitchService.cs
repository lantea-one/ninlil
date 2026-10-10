using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Services.Storage;

/// <summary>
///     This interface is the request side of a storage switch. It checks a target database and
///     saves the switch for the next start; <see cref="PendingStorageSwitchApplier" /> copies the
///     data at that start. <c>StorageController</c> uses it.
/// </summary>
public interface IStorageEngineSwitchService
{
    /// <summary>
    ///     This method reports the active storage engine, the saved switch (if any) and the
    ///     outcome of this start's switch.
    /// </summary>
    StorageStatus GetStatus();

    /// <summary>
    ///     This method checks that the target database can be reached and is not ahead of this
    ///     build, then saves the switch, replacing any earlier one. Nothing is copied and the
    ///     running process keeps its engine.
    /// </summary>
    /// <param name="request">Which engine to switch to, and how to reach it.</param>
    /// <param name="requestedBy">The user name recorded with the switch.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <exception cref="Ninlil.Dns.Common.Exceptions.ValidationException">
    ///     Thrown when the request is malformed, names MongoDB, already matches the active
    ///     storage, a read replica or cluster setting of the engine in use is configured, the target
    ///     cannot be reached within 15 seconds, or it holds migrations this build does not know.
    ///     No connection string is part of the message.
    /// </exception>
    Task<StorageSwitchScheduled> ScheduleSwitchAsync(
        StorageEngineSwitchRequest request,
        string requestedBy,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method deletes the saved switch.
    /// </summary>
    /// <returns>True when a switch was saved, false when there was none.</returns>
    bool CancelSwitch();
}
