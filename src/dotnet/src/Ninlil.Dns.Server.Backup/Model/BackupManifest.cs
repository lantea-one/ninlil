// Define our namespace.
namespace Ninlil.Dns.Server.Backup.Model;

/// <summary>
///     This record is the first entry written into every backup archive, and the first one read
///     back out of it. It carries just enough structural information for a restore to confirm,
///     before touching any real data, that the archive was actually produced by this exact
///     export format and is not, for example, a differently-shaped file that happened to decrypt
///     successfully with the supplied passphrase.
/// </summary>
/// <param name="FormatVersion">
///     The version of the entity/file layout this archive's own payload uses. Compared against
///     <see cref="Ninlil.Dns.Server.Backup.SystemBackupService.CurrentPayloadFormatVersion" />
///     during restore; a mismatch is rejected rather than guessed at.
/// </param>
/// <param name="CreatedAtUtc">The UTC date and time this archive was created.</param>
/// <param name="StorageEngine">
///     The name of the storage engine that was active when this archive was created, recorded
///     purely for an operator's own reference -- a restore works identically regardless of which
///     storage engine is active at the time it runs.
/// </param>
/// <param name="EntityCounts">
///     How many rows of each entity type this archive contains, keyed by the same name
///     <see cref="Ninlil.Dns.Server.Backup.Catalog.BackupEntityCatalog" /> uses for that
///     type's own database file inside the archive.
/// </param>
public sealed record BackupManifest(
    int FormatVersion,
    DateTimeOffset CreatedAtUtc,
    string StorageEngine,
    IReadOnlyDictionary<string, int> EntityCounts
);
