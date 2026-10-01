// Define our namespace.
namespace Ninlil.Dns.Server.Backup.Model;

/// <summary>
///     This record is the manifest (<c>manifest.json</c>) of a backup archive, written last when
///     an archive is created and read first on restore. It lets a restore check, before touching
///     any data, that the archive uses a payload format this server can import.
/// </summary>
/// <param name="FormatVersion">
///     The version of the payload layout of the archive. Compared with
///     <see cref="Ninlil.Dns.Server.Backup.SystemBackupService.CurrentPayloadFormatVersion" />
///     during restore; a mismatch is rejected rather than guessed at.
/// </param>
/// <param name="CreatedAtUtc">The UTC date and time this archive was created.</param>
/// <param name="StorageEngine">
///     The name of the storage engine active when the archive was created, for reference only.
///     A restore works the same whichever storage engine is active.
/// </param>
/// <param name="EntityCounts">
///     How many rows of each entity type the archive contains, keyed by the names
///     <see cref="Ninlil.Dns.Server.Backup.Catalog.BackupEntityCatalog" /> uses for the entity
///     files in the archive.
/// </param>
public sealed record BackupManifest(
    int FormatVersion,
    DateTimeOffset CreatedAtUtc,
    string StorageEngine,
    IReadOnlyDictionary<string, int> EntityCounts
);
