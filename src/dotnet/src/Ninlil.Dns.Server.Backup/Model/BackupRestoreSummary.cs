// Define our namespace.
namespace Ninlil.Dns.Server.Backup.Model;

/// <summary>
///     This record is returned when a restore has finished: the database has been wiped and
///     re-imported and the key ring, TLS certificate and runtime configuration file overwritten.
///     The running process keeps its loaded key ring, certificate and settings, so the caller
///     must be told that a restart is required.
/// </summary>
/// <param name="TotalEntitiesImported">
///     The total number of rows imported across every entity type, not counting identity
///     relationships.
/// </param>
/// <param name="EntityCounts">How many rows of each entity type were imported, by name.</param>
public sealed record BackupRestoreSummary(
    int TotalEntitiesImported,
    IReadOnlyDictionary<string, int> EntityCounts
);
