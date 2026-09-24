using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Services.Storage;

/// <summary>
///     This interface performs a real, in-process storage-engine switch -- no Docker, no
///     systemd, no process exit/respawn involved anywhere -- by backing up the currently active
///     engine, provisioning and migrating the target engine, flipping this process's own live
///     storage configuration onto it, and restoring the backup into it, all while this host stays
///     the same running operating-system process throughout. See
///     <see cref="StorageEngineSwitchService" />'s own doc comment for exactly how the underlying
///     mechanism works and why it needs no dependency-injection container rebuild.
/// </summary>
public interface IStorageEngineSwitchService
{
    /// <summary>
    ///     This method reports this host's own currently active storage engine and whether a
    ///     switch is in progress right now.
    /// </summary>
    StorageStatus GetStatus();

    /// <summary>
    ///     This method performs a live storage-engine switch, returning once this process is
    ///     already reading and writing through the new engine with every row carried across.
    /// </summary>
    /// <param name="request">Which engine to switch to, and how to reach it.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <exception cref="Ninlil.Dns.Common.Exceptions.ValidationException">
    ///     Thrown when the request is malformed, names an engine this live mechanism cannot
    ///     switch to or from (<see cref="Ninlil.Dns.Common.Enum.StorageEngineEnum.MongoDb" />),
    ///     or when the switch fails partway through -- in every failure case, this process is left
    ///     reading and writing through whichever engine was active before the call, with that
    ///     engine's own data untouched.
    /// </exception>
    /// <exception cref="Ninlil.Dns.Common.Exceptions.ConflictException">
    ///     Thrown when a switch is already in progress.
    /// </exception>
    Task<StorageEngineSwitchResult> SwitchEngineAsync(
        StorageEngineSwitchRequest request,
        CancellationToken stoppingToken = default
    );
}
