// Define our namespace.
namespace Ninlil.Dns.Server.Backup;

/// <summary>
///     This exception is thrown when a restore is requested while another one is still running.
///     Two restores would interleave their wipes and imports, so the second is refused instead of
///     queued; nothing has been changed when it is thrown.
/// </summary>
public sealed class BackupRestoreInProgressException()
    : InvalidOperationException(
        "Another restore is already running. Wait for it to finish, then try again."
    );
