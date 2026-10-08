// Define our namespace.
namespace Ninlil.Dns.Server.Backup.Tests;

/// <summary>
///     This class puts every test class that restores an archive into one xUnit collection. The
///     restore gate of <see cref="SystemBackupService" /> is static and refuses a second
///     concurrent restore, so classes that restore must not run in parallel.
/// </summary>
[CollectionDefinition(Name)]
public sealed class BackupRestoreCollection
{
    /// <summary>
    ///     This constant contains the collection name.
    /// </summary>
    public const string Name = "Backup restore (one at a time)";
}
